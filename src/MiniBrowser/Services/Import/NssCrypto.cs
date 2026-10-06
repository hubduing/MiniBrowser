using System.Formats.Asn1;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace MiniBrowser.Services.Import;

/// <summary>
/// Расшифровка данных Firefox по алгоритмам NSS без самой библиотеки NSS:
/// PKCS#12-лестница (3DES) и PBES2/PBKDF2 (AES-256) поверх key4.db и logins.json.
/// Логика соответствует эталону firepwd (lclevy/firepwd); код написан заново для C#.
/// </summary>
internal static class NssCrypto
{
    private const string OidPbe3Des = "1.2.840.113549.1.12.5.1.3";
    private const string OidPbes2 = "1.2.840.113549.1.5.13";

    /// <summary>Контрольная строка проверки мастер-пароля: «password-check» + PKCS7-пэдд 0x02×2.</summary>
    public static bool IsPasswordCheck(byte[] clearText)
    {
        var expected = new byte[16];
        "password-check"u8.CopyTo(expected);
        expected[14] = 2;
        expected[15] = 2;
        return clearText.AsSpan().SequenceEqual(expected);
    }

    /// <summary>
    /// Расшифровать DER-блоб ключа/проверки из key4.db (item2 или a11).
    /// Алгоритм выбирается по OID внутри ASN.1-дескриптора.
    /// </summary>
    public static byte[] DecryptPbe(byte[] der, byte[] globalSalt, byte[] masterPassword)
    {
        var reader = new AsnReader(der, AsnEncodingRules.DER);
        var outer = reader.ReadSequence();
        var algId = outer.ReadSequence();
        var oid = algId.ReadObjectIdentifier();
        var cipherText = outer.ReadOctetString();
        outer.ThrowIfNotEmpty();

        if (oid == OidPbe3Des)
        {
            // SEQUENCE { OID, SEQUENCE { OCTET STRING entrySalt, INTEGER iter } }
            var p = algId.ReadSequence();
            var entrySalt = p.ReadOctetString();
            p.ReadIntegerBytes();
            p.ThrowIfNotEmpty();
            algId.ThrowIfNotEmpty();
            return DecryptMoz3Des(globalSalt, masterPassword, entrySalt, cipherText);
        }

        if (oid == OidPbes2)
        {
            // SEQUENCE { kdf: SEQUENCE{OID pbkdf2, SEQUENCE{salt, iter, len, SEQUENCE{prf}}}, enc: SEQUENCE{OID aes256-cbc, OCTET STRING iv14} }
            var kdf = algId.ReadSequence();
            kdf.ReadObjectIdentifier();
            var kdfParams = kdf.ReadSequence();
            var entrySalt = kdfParams.ReadOctetString();
            var iterations = ReadInt(kdfParams.ReadIntegerBytes().Span);
            var keyLength = ReadInt(kdfParams.ReadIntegerBytes().Span);
            var prf = kdfParams.ReadSequence();
            prf.ReadObjectIdentifier();
            kdfParams.ThrowIfNotEmpty();
            kdf.ThrowIfNotEmpty();

            var encScheme = algId.ReadSequence();
            encScheme.ReadObjectIdentifier();
            var ivStored = encScheme.ReadOctetString();
            encScheme.ThrowIfNotEmpty();
            algId.ThrowIfNotEmpty();

            // NSS-кверка: IV хранится 14 байт, а используется 16 — с заголовком DER (04 0e).
            var iv = ivStored.Length == 14 ? new byte[] { 0x04, 0x0e }.Concat(ivStored).ToArray() : ivStored;

            var k = Cat(SHA1.HashData(Cat(globalSalt, masterPassword)));
            var key = Rfc2898DeriveBytes.Pbkdf2(k, entrySalt, iterations, HashAlgorithmName.SHA256, keyLength);
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            using var decryptor = aes.CreateDecryptor(key, iv);
            return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        }

        throw new InvalidDataException($"алгоритм NSS {oid} не поддерживается");
    }

    /// <summary>
    /// Расшифровать пару логин/пароль: DER-блоб из logins.json + мастер-ключ из a11.
    /// Длина ключа определяет шифр: 48 (32+16) → AES-256, иначе 3DES (24).
    /// </summary>
    public static string DecryptLogin(byte[] der, byte[] keyMaterial)
    {
        // Возможный трёхбайтовый префикс версии «v10»/«v11» перед DER.
        if (der.Length > 3 && der[0] == 'v' && der[1] == '1' && (der[2] == '0' || der[2] == '1'))
            der = der[3..];

        var reader = new AsnReader(der, AsnEncodingRules.DER);
        var outer = reader.ReadSequence();
        _ = outer.ReadOctetString(); // CKA_ID (f8…01) — для расшифровки не нужен
        var algId = outer.ReadSequence();
        _ = algId.ReadObjectIdentifier(); // des-ede3-cbc или aes256-cbc
        var iv = algId.ReadOctetString();
        algId.ThrowIfNotEmpty();
        var cipherText = outer.ReadOctetString();
        outer.ThrowIfNotEmpty();

        var useAes = keyMaterial.Length >= 48;
        if (useAes)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor(keyMaterial.AsSpan(0, 32).ToArray(), iv);
            var plain = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
            return Encoding.UTF8.GetString(plain);
        }

        if (keyMaterial.Length < 24)
            throw new InvalidDataException("недостаточно байт мастер-ключа Firefox");

        using var des = TripleDES.Create();
        des.Mode = CipherMode.CBC;
        des.Padding = PaddingMode.PKCS7;
        using var desDecryptor = des.CreateDecryptor(keyMaterial.AsSpan(0, 24).ToArray(), iv);
        var clear = desDecryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        return Encoding.UTF8.GetString(clear);
    }

    /// <summary>PKCS#12-лестница NSS (pbeWithSha1AndTripleDES-CBC), без пэддинга на выходе.</summary>
    private static byte[] DecryptMoz3Des(byte[] globalSalt, byte[] masterPassword, byte[] entrySalt, byte[] cipherText)
    {
        var hp = SHA1.HashData(Cat(globalSalt, masterPassword));
        // entrySalt дополняется нулями до 20 байт (pes), длиннее 20 — не усекается.
        var pes = entrySalt.Length < 20 ? Cat(entrySalt, new byte[20 - entrySalt.Length]) : entrySalt;
        var chp = SHA1.HashData(Cat(hp, entrySalt));
        using var hmac = new HMACSHA1(chp);
        var k1 = hmac.ComputeHash(Cat(pes, entrySalt));
        var tk = new HMACSHA1(chp).ComputeHash(pes);
        var k2 = new HMACSHA1(chp).ComputeHash(Cat(tk, entrySalt));
        var k = Cat(k1, k2); // 40 байт: 24 ключ + сдвиг + 8 iv
        using var des = TripleDES.Create();
        des.Mode = CipherMode.CBC;
        des.Padding = PaddingMode.None;
        using var decryptor = des.CreateDecryptor(k.AsSpan(0, 24).ToArray(), k.AsSpan(32, 8).ToArray());
        return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
    }

    private static int ReadInt(ReadOnlySpan<byte> bytes) =>
        (int)new BigInteger(bytes, isUnsigned: true, isBigEndian: true);

    private static byte[] Cat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}
