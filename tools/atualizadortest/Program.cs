using System.Security.Cryptography;
using System.Text;
using HellClockCoop.Atualizador;
// Testes do atualizador: assinatura e validação do manifesto, e troca dos arquivos com volta em caso de falha.
// uso: dotnet run --project tools/atualizadortest -c Release

int falhas = 0;
void Checar(bool ok, string o) { Console.WriteLine((ok ? "OK   " : "FALHA ") + o); if (!ok) falhas++; }

using var rsa = RSA.Create(3072);
RSAParameters publica = rsa.ExportParameters(false);
byte[] Assinar(byte[] dados) => Encoding.ASCII.GetBytes(Convert.ToBase64String(rsa.SignData(dados, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
byte[] Texto(string s) => Encoding.UTF8.GetBytes(s);
string H(byte[] b) => Manifesto.Sha256(b);

byte[] dllNova = Texto("plugin novo"), licencas = Texto("licenças"), atualizadorNovo = Texto("atualizador novo");
string ManifestoDe(string versao, params (string destino, byte[] dados)[] arquivos)
{
    var sb = new StringBuilder(Manifesto.Cabecalho + "\nversao " + versao + "\n");
    foreach (var (d, b) in arquivos) sb.Append($"arquivo {d} {b.Length} {H(b)}\n");
    return sb.ToString();
}
var completo = new[] { (Manifesto.DestinoPlugin, dllNova), ("plugins/HellClockCoop/LICENCAS.txt", licencas), (Manifesto.DestinoAtualizador, atualizadorNovo) };

// --- assinatura ---
byte[] bom = Texto(ManifestoDe("0.8.0", completo));
var m = Manifesto.Ler(bom, Assinar(bom), publica, out string erro);
Checar(m != null && m.VersaoTexto == "0.8.0" && m.Arquivos.Count == 3, $"manifesto assinado é aceito ({erro})");
Checar(m != null && m.Arquivos[0].Nome == "HellClockCoop.dll", "nome do arquivo avulso sem pastas");
Checar(Manifesto.VersaoSemVerificar(bom) == "0.8.0", "versão lida antes da verificação (só para achar a assinatura)");

byte[] adulterado = (byte[])bom.Clone(); adulterado[^5] ^= 1;
Checar(Manifesto.Ler(adulterado, Assinar(bom), publica, out erro) == null && erro == "assinatura inválida", "manifesto adulterado é recusado");
using (var outra = RSA.Create(3072))
    Checar(Manifesto.Ler(bom, Assinar(bom), outra.ExportParameters(false), out erro) == null, "assinatura de outra chave é recusada");
Checar(Manifesto.Ler(bom, Texto("não é base64!"), publica, out erro) == null, "assinatura malformada é recusada");
Checar(Manifesto.Ler(bom, Texto(""), publica, out erro) == null, "assinatura vazia é recusada");

// --- conteúdo inválido, mesmo bem assinado ---
void Recusa(string texto, string caso)
{
    byte[] b = Texto(texto);
    Checar(Manifesto.Ler(b, Assinar(b), publica, out string e) == null, $"recusa {caso} ({e})");
}
foreach (string destino in new[] { "plugins/Outro/x.dll", "plugins/HellClockCoop/../x.dll", "plugins/HellClockCoop/sub/x.dll",
                                   "patchers/Outro.dll", "C:/Windows/x.dll", "/etc/x", "plugins/HellClockCoop/", "plugins/HellClockCoop/x.",
                                   "plugins\\HellClockCoop\\x.dll", "core/BepInEx.dll", "../winhttp.dll" })
    Recusa(ManifestoDe("0.8.0", (Manifesto.DestinoPlugin, dllNova), (destino, licencas)), "destino " + destino);
Recusa(ManifestoDe("0.8.0", ("plugins/HellClockCoop/LICENCAS.txt", licencas)), "manifesto sem o plugin");
Recusa(ManifestoDe("0.8.0", (Manifesto.DestinoPlugin, dllNova), (Manifesto.DestinoPlugin, dllNova)), "destino repetido");
Recusa(ManifestoDe("0.8.0.1", completo), "versão com 4 partes");
Recusa(ManifestoDe("v0.8.0", completo), "versão com prefixo");
Recusa(ManifestoDe("0.8.0", completo).Replace(Manifesto.Cabecalho, "hellclockcoop-manifesto 2"), "formato desconhecido");
Recusa(ManifestoDe("0.8.0", completo).Replace(H(dllNova), H(dllNova).ToUpperInvariant()), "hash fora do padrão");
Recusa(ManifestoDe("0.8.0", completo).Replace($" {dllNova.Length} ", " -1 "), "tamanho negativo");
Recusa(ManifestoDe("0.8.0", completo) + "executar algo\n", "linha desconhecida");
Recusa(Manifesto.Cabecalho + "\n" + $"arquivo {Manifesto.DestinoPlugin} {dllNova.Length} {H(dllNova)}\n", "sem versão");

// --- chave pública embutida ---
RSAParameters embutida = ChavePublica.Parametros();
Checar(embutida.Modulus.Length == 384 && embutida.Exponent.Length == 3, "chave embutida é RSA 3072");
string privada = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hellclockcoop", "atualizador-privada.pem");
string pemAutor = File.Exists(privada) ? File.ReadAllText(privada) : null;
string senhaAutor = Environment.GetEnvironmentVariable("HELLCLOCKCOOP_SENHA_CHAVE");
if (pemAutor != null && (!pemAutor.Contains("ENCRYPTED") || senhaAutor != null))
{
    using var autor = RSA.Create();
    if (pemAutor.Contains("ENCRYPTED")) autor.ImportFromEncryptedPem(pemAutor, senhaAutor.AsSpan()); else autor.ImportFromPem(pemAutor);
    byte[] assinado = Encoding.ASCII.GetBytes(Convert.ToBase64String(autor.SignData(bom, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
    Checar(Manifesto.Ler(bom, assinado, embutida, out erro) != null, "chave embutida confere com a privada local do autor");
}
else Console.WriteLine("--   chave privada local ausente (ou com senha, sem HELLCLOCKCOOP_SENHA_CHAVE): conferência da chave embutida pulada");

// --- instalação: troca, guarda e volta ---
string NovaRaiz()
{
    string r = Path.Combine(Path.GetTempPath(), "hcc-atualizadortest-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(r, "plugins", "HellClockCoop"));
    Directory.CreateDirectory(Path.Combine(r, "patchers"));
    File.WriteAllText(Path.Combine(r, "plugins", "HellClockCoop", "HellClockCoop.dll"), "plugin velho");
    File.WriteAllText(Path.Combine(r, "patchers", "HellClockCoop.Atualizador.dll"), "atualizador velho");
    File.WriteAllText(Path.Combine(r, "plugins", "HellClockCoop", "extra.txt"), "fora do manifesto");
    return r;
}
string Ler(string raiz, string rel) => File.ReadAllText(Path.Combine(raiz, rel.Replace('/', Path.DirectorySeparatorChar)));
Dictionary<string, byte[]> Conteudo() => new() { [Manifesto.DestinoPlugin] = dllNova, ["plugins/HellClockCoop/LICENCAS.txt"] = licencas, [Manifesto.DestinoAtualizador] = atualizadorNovo };

string raiz = NovaRaiz();
bool ok = Instalacao.Aplicar(raiz, m, Conteudo(), _ => "0.8.0", out erro);
string trabalho = Instalacao.PastaDeTrabalho(raiz);
Checar(ok, $"instalação aplicada ({erro})");
Checar(Ler(raiz, Manifesto.DestinoPlugin) == "plugin novo" && Ler(raiz, Manifesto.DestinoAtualizador) == "atualizador novo"
       && Ler(raiz, "plugins/HellClockCoop/LICENCAS.txt") == "licenças", "arquivos novos no lugar");
Checar(Ler(raiz, "plugins/HellClockCoop/extra.txt") == "fora do manifesto", "arquivo fora do manifesto não é tocado");
Checar(File.ReadAllText(Path.Combine(trabalho, "anterior", "plugins", "HellClockCoop", "HellClockCoop.dll")) == "plugin velho"
       && File.ReadAllText(Path.Combine(trabalho, "anterior", "patchers", "HellClockCoop.Atualizador.dll")) == "atualizador velho", "versão anterior guardada");
Checar(!Directory.Exists(Path.Combine(trabalho, "novo")), "pasta de download limpa");
Directory.Delete(raiz, true);

raiz = NovaRaiz();
ok = Instalacao.Aplicar(raiz, m, Conteudo(), _ => "0.7.2", out erro);
Checar(!ok && erro.Contains("0.7.2"), $"validação da versão falha ({erro})");
Checar(Ler(raiz, Manifesto.DestinoPlugin) == "plugin velho" && Ler(raiz, Manifesto.DestinoAtualizador) == "atualizador velho", "falha volta a versão anterior");
Checar(!File.Exists(Path.Combine(raiz, "plugins", "HellClockCoop", "LICENCAS.txt")), "falha remove o arquivo que não existia antes");
Directory.Delete(raiz, true);

raiz = NovaRaiz();
var corrompido = Conteudo(); corrompido[Manifesto.DestinoAtualizador] = Texto("outra coisa");
ok = Instalacao.Aplicar(raiz, m, corrompido, _ => "0.8.0", out erro);
Checar(!ok && Ler(raiz, Manifesto.DestinoPlugin) == "plugin velho" && Ler(raiz, Manifesto.DestinoAtualizador) == "atualizador velho",
       $"conteúdo que não confere com o manifesto não troca nada ({erro})");
Directory.Delete(raiz, true);

raiz = NovaRaiz();
var soPlugin = new Dictionary<string, byte[]> { [Manifesto.DestinoPlugin] = dllNova, ["plugins/HellClockCoop/LICENCAS.txt"] = licencas };
File.WriteAllBytes(Path.Combine(raiz, "patchers", "HellClockCoop.Atualizador.dll"), atualizadorNovo); // já em dia
ok = Instalacao.Aplicar(raiz, m, soPlugin, _ => "0.8.0", out erro);
Checar(ok && !File.Exists(Path.Combine(trabalho = Instalacao.PastaDeTrabalho(raiz), "anterior", "patchers", "HellClockCoop.Atualizador.dll")),
       $"só os arquivos que mudaram são trocados ({erro})");
Directory.Delete(raiz, true);

// troca interrompida (jogo fechado depois de guardar a versão atual e antes de pôr a nova): volta o que faltar
raiz = NovaRaiz();
trabalho = Instalacao.PastaDeTrabalho(raiz);
string guardaPlugin = Path.Combine(trabalho, "anterior", "plugins", "HellClockCoop", "HellClockCoop.dll");
Directory.CreateDirectory(Path.GetDirectoryName(guardaPlugin));
File.Move(Path.Combine(raiz, "plugins", "HellClockCoop", "HellClockCoop.dll"), guardaPlugin);
Directory.CreateDirectory(Path.Combine(trabalho, "anterior", "patchers"));
File.WriteAllText(Path.Combine(trabalho, "anterior", "patchers", "HellClockCoop.Atualizador.dll"), "guardado");
File.WriteAllText(Path.Combine(trabalho, "anterior", "fora.dll"), "fora do permitido");
var restaurados = Instalacao.RecuperarTrocaInterrompida(raiz);
Checar(restaurados.Count == 1 && restaurados[0] == Manifesto.DestinoPlugin && Ler(raiz, Manifesto.DestinoPlugin) == "plugin velho",
       $"troca interrompida: plugin que faltava volta ({string.Join(", ", restaurados)})");
Checar(Ler(raiz, Manifesto.DestinoAtualizador) == "atualizador velho", "troca interrompida: arquivo que existe não é sobrescrito");
Checar(!File.Exists(Path.Combine(raiz, "fora.dll")), "troca interrompida: nada fora dos destinos permitidos é restaurado");
Checar(Instalacao.RecuperarTrocaInterrompida(raiz).Count == 0, "sem troca interrompida: não faz nada");
Directory.Delete(raiz, true);

Console.WriteLine(falhas == 0 ? "\nTODOS OS TESTES PASSARAM" : $"\n{falhas} FALHA(S)");
return falhas;
