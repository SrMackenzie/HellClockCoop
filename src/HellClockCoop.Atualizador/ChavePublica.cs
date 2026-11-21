using System;
using System.Security.Cryptography;

namespace HellClockCoop.Atualizador
{
    /// <summary>
    /// Chave pública do autor (RSA 3072) que assina o manifesto de cada release. A privada fica só na máquina de quem
    /// publica, fora do repositório (tools/assinar). Trocar esta chave exige que todos instalem o zip novo à mão.
    /// </summary>
    internal static class ChavePublica
    {
        private const string Modulo =
            "xeGrfTZ2+PN0N/BaGOLFDXnTyDjMwhEfqGb9Z9GDlBCq3mkDGR/rDCCVvn5IRxcRFGDZqg1hPeiReGI3+2tCEgrA3nxLGfd+H/z7LjQ9v4q1orP9" +
            "DIAcGsjueNoZghQnvkEhkhUV0+SziUzUsW56fNG+Enuwiifcz+o7V+xaiFiWWtPKu8aW2Jd65v+eoUxpCkIhsnSEM3e+slhjj5RqMC1r7pZlMnOF" +
            "sLSSyX7CjHhdWGj2xGXV1vlrdOqRqYNUadUP0+WdnUCaSr0+AcGaJa0QAObFIZdK7rS8MwF4uokQsDEZQ3hxWNVfBgWzjqNMVL9GidaVST1TdeG6" +
            "C8T+Oq8wDKcsUX3qF9HRx8puBn9KiGhOmS0GMCIksXttrgxdWR+uta3uD1r7HQ/mDZrY//vGuRxAFRALxYipQU2/gTMqO0VkJd0n+HWc1hoW9JaS" +
            "yHrtwZj4Ilirni8Gixd1CKkR+7wlvnXK8D0lo4wSdNkGK2aLCjpZo/QlJrH8MEUx";
        private const string Expoente = "AQAB";

        public static RSAParameters Parametros() => new RSAParameters
        {
            Modulus = Convert.FromBase64String(Modulo),
            Exponent = Convert.FromBase64String(Expoente),
        };
    }
}
