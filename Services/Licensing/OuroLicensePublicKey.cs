namespace Ouro.Services.Licensing
{
    /// <summary>
    /// RSA-2048 public key used to verify signed licenses.
    /// This is intentionally shipped with the client: only the matching private key,
    /// kept outside the repository, can issue new licenses.
    /// </summary>
    internal static class OuroLicensePublicKey
    {
        internal const string Xml =
            "<RSAKeyValue><Modulus>yqWF89uRqvRA7zwc8f8LYBm8OqS3hVHj58AWALkGw+pXyj/hZIIl/pmfXawjyGv2a5Rfekrh6CkGRkVwWXegv1RHLWXFwxTwg/hV78wXCkbpE47ySJvGutc6/0hq7IdJuTkxrSGsmBZPX91wps44Nj4ycTfod/n20FRS0GH9F1cni+APaPjQYPb/9jGqSbl/0o8SOw+V8gMdviOhH0YscKEtscjhzlERBjE2t1hnQVRLwoIx79/Hh0LBq56+F0TsLZZDZkcjbfFiTjH3YDc+IhcBTmmow0uFXTMeV/n83QRotyULksvv3ZbA6Wb6dYcCnSeZLwVPFm8MlKw5iX+5UQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    }
}
