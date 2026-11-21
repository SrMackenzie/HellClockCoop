using System.Security.Cryptography;
using System.Text;
// Assinatura do manifesto do atualizador (RSA 3072, PKCS#1 v1.5, SHA-256).
// A chave privada fica só na máquina de quem publica, FORA do repositório, cifrada com senha (PKCS#8 + AES-256);
// a pública vai embutida em src/HellClockCoop.Atualizador/ChavePublica.cs. Quem tiver a privada consegue publicar
// código que todos os jogadores instalam sozinhos: senha forte e uma cópia guardada fora do computador.
//
// uso:
//   assinar gerar-chave [arquivo-da-privada]   cria a chave (pede a senha) e mostra a pública para embutir
//   assinar proteger    [arquivo-da-privada]   cifra com senha uma chave que ainda está sem senha
//   assinar publica     [arquivo-da-privada]   mostra de novo a pública
//   assinar assinar <arquivo> [arquivo-da-privada]   grava <arquivo>.sig (Base64)
// Sem o arquivo da chave, usa %USERPROFILE%\.hellclockcoop\atualizador-privada.pem.
// A senha é pedida no console (sem eco) ou lida de HELLCLOCKCOOP_SENHA_CHAVE.

string padrao = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hellclockcoop", "atualizador-privada.pem");
if (args.Length == 0) return Uso();
switch (args[0])
{
    case "gerar-chave":
    {
        string destino = args.Length > 1 ? args[1] : padrao;
        if (File.Exists(destino)) { Console.Error.WriteLine($"Já existe uma chave em {destino}; não sobrescrevo."); return 1; }
        string senha = SenhaNova();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destino)));
        using var rsa = RSA.Create(3072);
        File.WriteAllText(destino, Cifrar(rsa, senha));
        Console.WriteLine($"Chave privada gravada em {destino}. Guarde uma cópia fora deste computador; nunca a commite.");
        MostrarPublica(rsa);
        return 0;
    }
    case "proteger":
    {
        string caminho = args.Length > 1 ? args[1] : padrao;
        string pem = File.ReadAllText(caminho);
        if (pem.Contains("ENCRYPTED PRIVATE KEY")) { Console.WriteLine("A chave já tem senha."); return 0; }
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        string temporario = caminho + ".tmp";
        File.WriteAllText(temporario, Cifrar(rsa, SenhaNova()));
        File.Move(temporario, caminho, overwrite: true);
        Console.WriteLine($"Chave cifrada com senha em {caminho}. A pública não muda.");
        return 0;
    }
    case "publica":
    {
        using var rsa = Carregar(args.Length > 1 ? args[1] : padrao);
        MostrarPublica(rsa);
        return 0;
    }
    case "assinar":
    {
        if (args.Length < 2) return Uso();
        using var rsa = Carregar(args.Length > 2 ? args[2] : padrao);
        byte[] dados = File.ReadAllBytes(args[1]);
        byte[] assinatura = rsa.SignData(dados, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (!rsa.VerifyData(dados, assinatura, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) { Console.Error.WriteLine("A assinatura não confere."); return 1; }
        File.WriteAllText(args[1] + ".sig", Convert.ToBase64String(assinatura));
        Console.WriteLine($"OK: {args[1]}.sig");
        return 0;
    }
    default:
        return Uso();
}

static string Cifrar(RSA rsa, string senha) =>
    rsa.ExportEncryptedPkcs8PrivateKeyPem(senha.AsSpan(), new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));

static RSA Carregar(string caminho)
{
    if (!File.Exists(caminho)) throw new FileNotFoundException($"Chave privada não encontrada em {caminho}. Gere com: assinar gerar-chave");
    string pem = File.ReadAllText(caminho);
    var rsa = RSA.Create();
    if (pem.Contains("ENCRYPTED PRIVATE KEY"))
    {
        rsa.ImportFromEncryptedPem(pem, Senha("Senha da chave do atualizador: ").AsSpan());
    }
    else
    {
        Console.Error.WriteLine("AVISO: a chave privada está sem senha. Rode: assinar proteger");
        rsa.ImportFromPem(pem);
    }
    return rsa;
}

static string SenhaNova()
{
    string s = Senha("Senha nova da chave (mínimo 12 caracteres): ");
    if (s.Length < 12) throw new ArgumentException("Senha curta demais.");
    if (Environment.GetEnvironmentVariable("HELLCLOCKCOOP_SENHA_CHAVE") == null && Senha("Repita a senha: ") != s)
        throw new ArgumentException("As senhas não conferem.");
    return s;
}

static string Senha(string pergunta)
{
    string env = Environment.GetEnvironmentVariable("HELLCLOCKCOOP_SENHA_CHAVE");
    if (env != null) return env;
    Console.Error.Write(pergunta);
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var sb = new StringBuilder();
    while (true)
    {
        ConsoleKeyInfo k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) break;
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
        sb.Append(k.KeyChar);
    }
    Console.Error.WriteLine();
    return sb.ToString();
}

static void MostrarPublica(RSA rsa)
{
    RSAParameters p = rsa.ExportParameters(false);
    Console.WriteLine("Chave pública (cole em src/HellClockCoop.Atualizador/ChavePublica.cs):");
    Console.WriteLine("Modulo = \"" + Convert.ToBase64String(p.Modulus) + "\"");
    Console.WriteLine("Expoente = \"" + Convert.ToBase64String(p.Exponent) + "\"");
}

static int Uso()
{
    Console.Error.WriteLine("uso: assinar gerar-chave|proteger|publica [chave] | assinar assinar <arquivo> [chave]");
    return 2;
}
