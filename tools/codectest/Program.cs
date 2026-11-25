using System; using System.Collections.Generic; using System.Reflection; using System.Linq;
// Testa os codecs por reflexão (as classes são internal).
var asm = Assembly.LoadFrom(args.Length > 0 ? args[0] : "../../build/HellClockCoop.dll");
Type T(string n) => asm.GetType(n, true);
int falhas = 0;
void Checar(bool ok, string o) { Console.WriteLine((ok ? "OK   " : "FALHA ") + o); if (!ok) falhas++; }

// Hello
var tHello = T("HellClockCoop.Net.Hello");
var h = Activator.CreateInstance(tHello);
tHello.GetField("Protocolo").SetValue(h, 5); tHello.GetField("CursedWar").SetValue(h, true); tHello.GetField("Deluxe").SetValue(h, false); tHello.GetField("VersaoMod").SetValue(h, "0.2.0");
tHello.GetField("VersaoJogo").SetValue(h, "2.1.1.1"); tHello.GetField("Nome").SetValue(h, "SrMackenzie ção"); tHello.GetField("JaTeOuvi").SetValue(h, true);
var bh = (byte[])tHello.GetMethod("Serializar").Invoke(h, null);
var h2 = tHello.GetMethod("Desserializar").Invoke(null, new object[] { bh });
Checar(bh[0] == 10, $"Hello tipo=10 ({bh.Length} bytes)");
foreach (var f in new[] { "Protocolo", "VersaoMod", "VersaoJogo", "Nome", "JaTeOuvi", "Deluxe", "CursedWar" })
    Checar(Equals(tHello.GetField(f).GetValue(h), tHello.GetField(f).GetValue(h2)), "Hello." + f + " = " + tHello.GetField(f).GetValue(h2));

// Aparencia
var tAp = T("HellClockCoop.Net.Aparencia");
var ap = Activator.CreateInstance(tAp);
var nos = (System.Collections.IList)tAp.GetField("Nos").GetValue(ap);
nos.Add(new System.Collections.Generic.KeyValuePair<string, bool>("Bota", true));
nos.Add(new System.Collections.Generic.KeyValuePair<string, bool>("Bota/Bota_lvl_03", true));
nos.Add(new System.Collections.Generic.KeyValuePair<string, bool>("Chapeu/Chapeu_lvl_01", false));
var ba = (byte[])tAp.GetMethod("Serializar").Invoke(ap, null);
var ap2 = tAp.GetMethod("Desserializar").Invoke(null, new object[] { ba });
Checar((bool)tAp.GetMethod("IgualA").Invoke(ap, new[] { ap2 }), $"Aparencia ida e volta ({ba.Length} bytes)");

// PlayerSnapshot (sem chamar código nativo da Unity: só structs Vector3/Quaternion e Mathf puro)
var tS = T("HellClockCoop.Net.PlayerSnapshot");
var tF = T("HellClockCoop.Net.AnimatorFrame");
var s = Activator.CreateInstance(tS);
tS.GetField("Sequencia").SetValue(s, (ushort)65535); tS.GetField("Tempo").SetValue(s, 123.5f);
tS.GetField("CenaHash").SetValue(s, 8); tS.GetField("Andar").SetValue(s, 3u); tS.GetField("Morto").SetValue(s, true);
tS.GetField("Vida").SetValue(s, 0.375f);
tS.GetField("Barreira").SetValue(s, 0.625f);
tS.GetField("Posicao").SetValue(s, new UnityEngine.Vector3(1, 2, 3));
tS.GetField("Rotacao").SetValue(s, new UnityEngine.Quaternion(0, 0.7071f, 0, 0.7071f));
tS.GetField("RotacaoModelo").SetValue(s, new UnityEngine.Quaternion(0, 0, 0, 1));
var ativos = (bool[])tS.GetField("AnimatorAtivo").GetValue(s); ativos[0] = true; ativos[2] = true;
var frames = (Array)tS.GetField("Animators").GetValue(s);
var f0 = frames.GetValue(0);
tF.GetField("StateHashes").SetValue(f0, new[] { 111, -222, 333, 444 });
tF.GetField("NormalizedTimes").SetValue(f0, new[] { 0.25f, 2.5f, 0f, 1f });
tF.GetField("LayerWeights").SetValue(f0, new[] { 1f, 0.5f, 0f, 1f });
tF.GetField("Floats").SetValue(f0, new[] { 1.5f, 0.75f, 2f });
tF.GetField("Ints").SetValue(f0, new[] { 7 });
tF.GetField("Bools").SetValue(f0, new[] { true, false, true });
var f2 = frames.GetValue(2);
tF.GetField("StateHashes").SetValue(f2, new[] { 999 }); tF.GetField("NormalizedTimes").SetValue(f2, new[] { 0.5f });
tF.GetField("LayerWeights").SetValue(f2, new[] { 1f });
tS.GetField("MascaraOssos").SetValue(s, (byte)0b10101);
var ossos = (UnityEngine.Quaternion[])tS.GetField("RotacoesOssos").GetValue(s);
ossos[0] = new UnityEngine.Quaternion(0.1f, 0.2f, 0.3f, 0.9f); ossos[2] = new UnityEngine.Quaternion(0, 0, 0.5f, 0.86f); ossos[4] = new UnityEngine.Quaternion(0, 0, 0, 1);
var bs = (byte[])tS.GetMethod("Serializar").Invoke(s, null);
var s2 = tS.GetMethod("Desserializar").Invoke(null, new object[] { bs });
Checar(bs[0] == 1 && bs[1] == 5, $"Snapshot tipo=1 versão=5 ({bs.Length} bytes)");
Checar(Math.Abs((float)tS.GetField("Vida").GetValue(s2) - 0.375f) < 1f / 65535f, "Snapshot.Vida (fração em ushort)");
var sVida = Activator.CreateInstance(tS); tS.GetField("Vida").SetValue(sVida, 1.7f); // fora de 0..1: chega presa em 1
var sVida2 = tS.GetMethod("Desserializar").Invoke(null, new object[] { (byte[])tS.GetMethod("Serializar").Invoke(sVida, null) });
Checar((float)tS.GetField("Vida").GetValue(sVida2) == 1f, "Snapshot.Vida presa em 0..1");
Checar(Math.Abs((float)tS.GetField("Barreira").GetValue(s2) - 0.625f) < 1f / 65535f, "Snapshot.Barreira (convicção, fração em ushort, 0.28.0)");
Checar((float)tS.GetField("Barreira").GetValue(sVida2) == 0f, "Snapshot.Barreira começa em 0 (sem barreira)");
var sBar = Activator.CreateInstance(tS); tS.GetField("Barreira").SetValue(sBar, 2.5f);
var sBar2 = tS.GetMethod("Desserializar").Invoke(null, new object[] { (byte[])tS.GetMethod("Serializar").Invoke(sBar, null) });
Checar((float)tS.GetField("Barreira").GetValue(sBar2) == 1f, "Snapshot.Barreira presa em 0..1");
var sBar3 = Activator.CreateInstance(tS); tS.GetField("Barreira").SetValue(sBar3, -0.4f);
var sBar4 = tS.GetMethod("Desserializar").Invoke(null, new object[] { (byte[])tS.GetMethod("Serializar").Invoke(sBar3, null) });
Checar((float)tS.GetField("Barreira").GetValue(sBar4) == 0f, "Snapshot.Barreira negativa chega 0");
var bsAntigo = (byte[])bs.Clone(); bsAntigo[1] = 4;
try { tS.GetMethod("Desserializar").Invoke(null, new object[] { bsAntigo }); Checar(false, "Snapshot formato 4 recusado"); }
catch (System.Reflection.TargetInvocationException e) { Checar(e.InnerException is FormatException, "Snapshot formato 4 (sem barreira) recusado"); }
foreach (var f in new[] { "Sequencia", "Tempo", "CenaHash", "Andar", "Morto", "Posicao", "Rotacao", "RotacaoModelo", "MascaraOssos" })
    Checar(Equals(tS.GetField(f).GetValue(s), tS.GetField(f).GetValue(s2)), "Snapshot." + f);
Checar(((bool[])tS.GetField("AnimatorAtivo").GetValue(s2)).SequenceEqual(new[] { true, false, true }), "Snapshot.AnimatorAtivo");
var g0 = ((Array)tS.GetField("Animators").GetValue(s2)).GetValue(0);
Checar(((int[])tF.GetField("StateHashes").GetValue(g0)).SequenceEqual(new[] { 111, -222, 333, 444 }), "Frame0.StateHashes");
Checar(((float[])tF.GetField("NormalizedTimes").GetValue(g0)).SequenceEqual(new[] { 0.25f, 2.5f, 0f, 1f }), "Frame0.NormalizedTimes");
var w = (float[])tF.GetField("LayerWeights").GetValue(g0);
Checar(Math.Abs(w[1] - 0.5f) < 0.005f && w[0] == 1f && w[2] == 0f, $"Frame0.LayerWeights ≈ (1, {w[1]:0.000}, 0, 1) — 1 byte cada");
Checar(((float[])tF.GetField("Floats").GetValue(g0)).SequenceEqual(new[] { 1.5f, 0.75f, 2f }), "Frame0.Floats");
Checar(((int[])tF.GetField("Ints").GetValue(g0)).SequenceEqual(new[] { 7 }), "Frame0.Ints");
Checar(((bool[])tF.GetField("Bools").GetValue(g0)).SequenceEqual(new[] { true, false, true }), "Frame0.Bools");
var g2 = ((Array)tS.GetField("Animators").GetValue(s2)).GetValue(2);
Checar(((int[])tF.GetField("StateHashes").GetValue(g2)).SequenceEqual(new[] { 999 }), "Frame2 (lança) presente; Frame1 ausente");
var o2 = (UnityEngine.Quaternion[])tS.GetField("RotacoesOssos").GetValue(s2);
Checar(o2[0].Equals(ossos[0]) && o2[2].Equals(ossos[2]) && o2[4].Equals(ossos[4]), "Snapshot.RotacoesOssos (máscara 10101)");

// Formato errado é recusado
try { tS.GetMethod("Desserializar").Invoke(null, new object[] { bh }); Checar(false, "Hello passado como snapshot deveria falhar"); }
catch (TargetInvocationException e) { Checar(e.InnerException is FormatException, "Hello passado como snapshot é recusado (" + e.InnerException.GetType().Name + ")"); }
// Fase 2: mensagens da run compartilhada
object IdaVolta(string tipo, (string campo, object valor)[] campos, byte tipoByte)
{
    var t = T("HellClockCoop.Net." + tipo);
    var o = Activator.CreateInstance(t);
    foreach (var (c, v) in campos) t.GetField(c).SetValue(o, v);
    var b = (byte[])t.GetMethod("Serializar").Invoke(o, null);
    var o2 = t.GetMethod("Desserializar").Invoke(null, new object[] { b });
    Checar(b[0] == tipoByte, $"{tipo} tipo={tipoByte} ({b.Length} bytes)");
    foreach (var (c, v) in campos) Checar(Equals(t.GetField(c).GetValue(o2), v), $"{tipo}.{c} = {t.GetField(c).GetValue(o2)}");
    return o2;
}
IdaVolta("PlanoAndar", new (string, object)[] {
    ("IndiceAndar", 7u), ("Primeiro", true), ("DungeonHash", 12345), ("AndarEspecial", 2), ("BlocoHash", 3),
    ("IndiceLocal", 2u), ("TemSementeBoss", true), ("SementeBoss", 4000000000u), ("BossForcado", -1), ("DefinicaoHash", 8), ("EhBoss", true),
    ("SementeCena", 3735928559u) }, 20);
IdaVolta("SpawnAndar", new (string, object)[] { ("IndiceAndar", 3u), ("IndiceSpawn", 2) }, 21);
IdaVolta("TimerSync", new (string, object)[] { ("Timer", 417.25f), ("TimerRelaxado", 999.5f) }, 22);
IdaVolta("TimerBonus", new (string, object)[] { ("Segundos", 15f) }, 23);
IdaVolta("FimRun", new (string, object)[] { ("Motivo", 3) }, 24);
IdaVolta("PedidoAvanco", new (string, object)[] { ("IndiceAndar", 6u), ("Andares", 1u) }, 25); // 0.15.0
IdaVolta("Reanimando", new (string, object)[] { ("Alvo", 76561198000000123UL), ("IndiceAndar", 4u), ("Estado", (byte)1), ("Fracao", 0.375f) }, 26); // 0.16.0
IdaVolta("Reanimou", new (string, object)[] { ("Alvo", 76561198000000123UL), ("IndiceAndar", 4u), ("Ordem", (byte)2) }, 27); // 0.16.0
IdaVolta("Marcador", new (string, object)[] { ("Cena", -123456789), ("IndiceAndar", 4294967295u), ("X", 12.5f), ("Y", -0.25f), ("Z", 99999.5f) }, 28); // 0.17.0
IdaVolta("ForaDaRun", new (string, object)[] { ("Fora", true) }, 29); // 0.19.0
IdaVolta("ForaDaRun", new (string, object)[] { ("Fora", false) }, 29);
IdaVolta("PedidoTp", new (string, object)[] { ("Alvo", 76561198000000123UL), ("IndiceAndar", 6u) }, 16); // 0.29.0
IdaVolta("RespostaTp", new (string, object)[] { ("Alvo", 76561198000000123UL), ("Motivo", (byte)5), ("Recarga", 42.5f) }, 17); // 0.29.0
try { T("HellClockCoop.Net.FimRun").GetMethod("Desserializar").Invoke(null, new object[] { new byte[] { 20, 0, 0, 0, 0 } }); Checar(false, "PlanoAndar lido como FimRun deveria falhar"); }
catch (TargetInvocationException e) { Checar(e.InnerException is FormatException, "Tipo errado é recusado (FimRun ≠ PlanoAndar)"); }

// Fase 3a: inimigos
IdaVolta("InimigoSpawn", new (string, object)[] {
    ("Id", (ushort)513), ("Andar", 4u), ("DefinicaoHash", -77), ("Tipo", (byte)2), ("ArquetipoHash", 91), ("Sobrevivencia", true),
    ("ParDeBoss", false), ("BarraDeVida", true), ("EfeitoSpawn", (byte)2), ("AtrasoSpawn", 0.75f), ("Yaw", 271.5f), ("JaNasceu", true), ("Vida", 0.42f),
    ("Posicao", new UnityEngine.Vector3(1.5f, -2f, 300.25f)), ("OrigemSpawn", new UnityEngine.Vector3(9f, 0f, -9f)) }, 30);
{
    var t = T("HellClockCoop.Net.InimigoSpawn"); var o = Activator.CreateInstance(t);
    t.GetField("Afixos").SetValue(o, new[] { 5, -6, 70000 });
    var oa = t.GetMethod("Desserializar").Invoke(null, new object[] { (byte[])t.GetMethod("Serializar").Invoke(o, null) });
    Checar(((int[])t.GetField("Afixos").GetValue(oa)).SequenceEqual(new[] { 5, -6, 70000 }), "InimigoSpawn.Afixos");
}
// 0.10.1 (protocolo 13): multiplicador de XP (ondas de sobrevivência) e tesouro próprio vão para a réplica
IdaVolta("InimigoSpawn", new (string, object)[] { ("Id", (ushort)7), ("Ajustes", (byte)12), ("MultiplicadorXp", 1.75f), ("TesouroHash", -123456) }, 30);
{
    var t = T("HellClockCoop.Net.InimigoSpawn");
    Checar(Equals(t.GetField("MultiplicadorXp").GetValue(Activator.CreateInstance(t)), 1f), "InimigoSpawn.MultiplicadorXp começa em 1");
}
IdaVolta("InimigoAjuste", new (string, object)[] { ("Id", (ushort)40000), ("Ajuste", (byte)12), ("MultiplicadorXp", 2.5f), ("TesouroHash", 77) }, 36);
IdaVolta("InimigoAjuste", new (string, object)[] { ("Id", (ushort)3), ("Ajuste", (byte)1) }, 36);
{
    // Ajuste que chega antes da réplica nascer: junta no spawn pendente sem apagar o que já veio
    var tS2 = T("HellClockCoop.Net.InimigoSpawn"); var tA = T("HellClockCoop.Net.InimigoAjuste");
    var sp = Activator.CreateInstance(tS2);
    tS2.GetField("Ajustes").SetValue(sp, (byte)1);
    var aXp = Activator.CreateInstance(tA); tA.GetField("Ajuste").SetValue(aXp, (byte)4); tA.GetField("MultiplicadorXp").SetValue(aXp, 3f);
    tA.GetMethod("JuntarEm").Invoke(aXp, new[] { sp });
    var aTc = Activator.CreateInstance(tA); tA.GetField("Ajuste").SetValue(aTc, (byte)8); tA.GetField("TesouroHash").SetValue(aTc, 99); tA.GetField("MultiplicadorXp").SetValue(aTc, 9f);
    tA.GetMethod("JuntarEm").Invoke(aTc, new[] { sp });
    Checar(Equals(tS2.GetField("Ajustes").GetValue(sp), (byte)13), "InimigoAjuste.JuntarEm soma as marcas (1|4|8)");
    Checar(Equals(tS2.GetField("MultiplicadorXp").GetValue(sp), 3f), "InimigoAjuste.JuntarEm: XP só muda com a marca Xp");
    Checar(Equals(tS2.GetField("TesouroHash").GetValue(sp), 99), "InimigoAjuste.JuntarEm: tesouro só muda com a marca Tesouro");
}
IdaVolta("InimigoMorte", new (string, object)[] { ("Id", (ushort)65000), ("SemDrop", true) }, 31);
IdaVolta("InimigoSumiu", new (string, object)[] { ("Id", (ushort)9) }, 32);
IdaVolta("AndarLimpo", new (string, object)[] { ("Andar", 12u) }, 35);
{
    // Estado (protocolo 25): cada item traz só os campos da máscara; as marcas nasceu/agressivo vão sempre
    var tE = T("HellClockCoop.Net.InimigosEstado"); var tI = T("HellClockCoop.Net.InimigosEstado+Item");
    var tFr = T("HellClockCoop.Net.AnimatorFrame");
    byte K(string n) => (byte)tE.GetField(n).GetValue(null);
    byte POS = K("TemPosicao"), YAW = K("TemYaw"), VIDA = K("TemVida"), ANIM = K("TemAnimacao"), TODOS = K("TodosOsCampos");
    object Quadro(int estado, float tempo, float peso, float[] floats, int[] ints = null, bool[] bools = null)
    {
        var fr = Activator.CreateInstance(tFr);
        tFr.GetField("StateHashes").SetValue(fr, new[] { estado }); tFr.GetField("NormalizedTimes").SetValue(fr, new[] { tempo });
        tFr.GetField("LayerWeights").SetValue(fr, new[] { peso }); tFr.GetField("Floats").SetValue(fr, floats);
        if (ints != null) tFr.GetField("Ints").SetValue(fr, ints);
        if (bools != null) tFr.GetField("Bools").SetValue(fr, bools);
        return fr;
    }
    object Item(ushort id, byte campos, UnityEngine.Vector3 pos, float yaw, float vida, bool nasceu, bool agressivo, object anim)
    {
        var i = Activator.CreateInstance(tI);
        tI.GetField("Id").SetValue(i, id); tI.GetField("Campos").SetValue(i, campos);
        tI.GetField("Posicao").SetValue(i, pos); tI.GetField("Yaw").SetValue(i, yaw); tI.GetField("Vida").SetValue(i, vida);
        tI.GetField("JaNasceu").SetValue(i, nasceu); tI.GetField("Agressivo").SetValue(i, agressivo); tI.GetField("Animacao").SetValue(i, anim);
        return i;
    }
    object C(object item, string campo) => tI.GetField(campo).GetValue(item);
    var e = Activator.CreateInstance(tE); tE.GetField("Sequencia").SetValue(e, (ushort)65535);
    var lista = (System.Collections.IList)tE.GetField("Itens").GetValue(e);
    var p1 = new UnityEngine.Vector3(1, 2f, -3.5f);
    lista.Add(Item(100, TODOS, p1, 90f, 0.5f, true, true, Quadro(42, 0.3f, 1f, new[] { 0.9f })));   // completo: 2+1+12+2+2+21
    lista.Add(Item(101, POS, p1, 90f, 0.5f, false, false, null));                                  // só posição: 2+1+12
    lista.Add(Item(102, (byte)(YAW | VIDA), p1, 270f, 0.25f, true, false, null));                  // giro e vida: 2+1+2+2
    lista.Add(Item(103, 0, p1, 90f, 0.5f, false, true, null));                                     // só as marcas: 2+1
    lista.Add(Item(104, TODOS, p1, 90f, 0.5f, true, false, null));                                 // sem Animator: a animação não vai
    var b = (byte[])tE.GetMethod("Serializar").Invoke(e, null);
    var e2 = tE.GetMethod("Desserializar").Invoke(null, new object[] { b });
    var l2 = (System.Collections.IList)tE.GetField("Itens").GetValue(e2);
    int contagem = (int)tE.GetField("PosicaoDaContagem").GetValue(null);
    Checar(b[0] == 33 && (ushort)tE.GetField("Sequencia").GetValue(e2) == 65535 && l2.Count == 5 && b[contagem] == 5, $"InimigosEstado tipo=33, sequência de 16 bits, 5 itens e a contagem no byte {contagem} ({b.Length} bytes)");
    Checar(b.Length == 4 + 40 + 15 + 7 + 3 + 19, "InimigosEstado: item completo 40 bytes, só posição 15, giro e vida 7, só marcas 3, sem Animator 19");
    Checar((byte)C(l2[0], "Campos") == TODOS && ((UnityEngine.Vector3)C(l2[0], "Posicao")).Equals(p1) && Math.Abs((float)C(l2[0], "Yaw") - 90f) < 0.01f
        && Math.Abs((float)C(l2[0], "Vida") - 0.5f) < 0.0001f && (bool)C(l2[0], "JaNasceu") && (bool)C(l2[0], "Agressivo")
        && ((int[])tFr.GetField("StateHashes").GetValue(C(l2[0], "Animacao"))).SequenceEqual(new[] { 42 }), "InimigosEstado.Item completo: posição, giro e vida (16 bits), marcas e animação");
    Checar((byte)C(l2[1], "Campos") == POS && ((UnityEngine.Vector3)C(l2[1], "Posicao")).Equals(p1) && C(l2[1], "Animacao") == null
        && !(bool)C(l2[1], "JaNasceu") && !(bool)C(l2[1], "Agressivo"), "InimigosEstado.Item só com posição");
    Checar((byte)C(l2[2], "Campos") == (byte)(YAW | VIDA) && Math.Abs((float)C(l2[2], "Yaw") - 270f) < 0.01f && Math.Abs((float)C(l2[2], "Vida") - 0.25f) < 0.0001f
        && (bool)C(l2[2], "JaNasceu") && !(bool)C(l2[2], "Agressivo"), "InimigosEstado.Item só com giro e vida");
    Checar((byte)C(l2[3], "Campos") == 0 && !(bool)C(l2[3], "JaNasceu") && (bool)C(l2[3], "Agressivo"), "InimigosEstado.Item só com as marcas (agressivo)");
    Checar((byte)C(l2[4], "Campos") == (byte)(TODOS & ~ANIM) && C(l2[4], "Animacao") == null, "InimigosEstado.Item completo sem Animator: a máscara chega sem animação");
    // Pacote forjado: bits que o protocolo não usa são ignorados; item cortado no meio não é aceito
    var forjado = new byte[] { 33, 1, 0, 1, 9, 0, (byte)(64 | 128 | 1) };
    var ef = (System.Collections.IList)tE.GetField("Itens").GetValue(tE.GetMethod("Desserializar").Invoke(null, new object[] { forjado }));
    Checar(ef.Count == 1 && (ushort)C(ef[0], "Id") == 9 && (byte)C(ef[0], "Campos") == 0 && (bool)C(ef[0], "JaNasceu") && !(bool)C(ef[0], "Agressivo"), "InimigosEstado: bits 64 e 128 do byte de marcas são ignorados (item só com a marca nasceu)");
    bool recusou = false;
    try { tE.GetMethod("Desserializar").Invoke(null, new object[] { new byte[] { 33, 1, 0, 2, 9, 0, POS, 1, 2, 3 } }); }
    catch (TargetInvocationException ex) when (ex.InnerException is System.IO.EndOfStreamException) { recusou = true; }
    Checar(recusou, "InimigosEstado: pacote cortado no meio de um item (ou com menos itens que a contagem) dá EndOfStreamException, que o Receber trata");

    // EstadoEnviado: o que o host manda de um inimigo a cada envio
    var tEn = T("HellClockCoop.Net.EstadoEnviado");
    (byte campos, bool enviar) Decidir(object memoria, object item, bool completo)
    {
        var a = new object[] { item, completo, false };
        byte c = (byte)tEn.GetMethod("Decidir").Invoke(memoria, a);
        return (c, (bool)a[2]);
    }
    var m = Activator.CreateInstance(tEn);
    object At(UnityEngine.Vector3 pos, float yaw, float vida, bool nasceu, bool agressivo, object anim) => Item(7, 0, pos, yaw, vida, nasceu, agressivo, anim);
    var q0 = new UnityEngine.Vector3(10f, 0f, 10f);
    var r1 = Decidir(m, At(q0, 90f, 1f, false, false, Quadro(42, 0.3f, 1f, new[] { 0.5f })), false);
    Checar(r1.enviar && r1.campos == TODOS, "EstadoEnviado: o primeiro envio de um inimigo leva tudo");
    var r2 = Decidir(m, At(q0, 90f, 1f, false, false, Quadro(42, 0.4f, 1f, new[] { 0.5f })), false);
    Checar(r2.enviar && r2.campos == TODOS, "EstadoEnviado: o que acabou de ir é repetido no envio seguinte (pacote perdido)");
    var r3 = Decidir(m, At(q0, 90f, 1f, false, false, Quadro(42, 0.5f, 1f, new[] { 0.5f })), false);
    Checar(!r3.enviar && r3.campos == 0, "EstadoEnviado: parado, na mesma animação (só o tempo andou), fica fora do pacote");
    var r4 = Decidir(m, At(q0 + new UnityEngine.Vector3(0.01f, 0f, 0f), 90.5f, 1f, false, false, Quadro(42, 0.6f, 1f, new[] { 0.51f })), false);
    Checar(!r4.enviar, "EstadoEnviado: 1 cm, meio grau e 0,01 no parâmetro ficam abaixo do mínimo");
    var q1 = q0 + new UnityEngine.Vector3(0.5f, 0f, 0f);
    var r5 = Decidir(m, At(q1, 90f, 1f, false, false, Quadro(42, 0.7f, 1f, new[] { 0.5f })), false);
    Checar(r5.enviar && r5.campos == POS, "EstadoEnviado: andou 50 cm, vai só a posição");
    var r6 = Decidir(m, At(q1, 90f, 1f, false, false, Quadro(42, 0.8f, 1f, new[] { 0.5f })), false);
    var r7 = Decidir(m, At(q1, 90f, 1f, false, false, Quadro(42, 0.9f, 1f, new[] { 0.5f })), false);
    Checar(r6.enviar && r6.campos == POS && !r7.enviar, "EstadoEnviado: parou, a posição vai mais uma vez e depois nada");
    // deriva lenta: 1 cm por envio não passa do mínimo a cada envio, mas passa em relação ao último valor enviado
    bool derivou = false;
    for (int k = 1; k <= 3; k++) derivou |= Decidir(m, At(q1 + new UnityEngine.Vector3(0.01f * k, 0f, 0f), 90f, 1f, false, false, Quadro(42, 1f, 1f, new[] { 0.5f })), false).campos == POS;
    Checar(derivou, "EstadoEnviado: deriva lenta (1 cm por envio) vai quando soma mais de 2 cm desde a última posição enviada");
    Decidir(m, At(q1, 90f, 1f, false, false, Quadro(42, 1f, 1f, new[] { 0.5f })), true); // completo: põe a memória em q1
    Decidir(m, At(q1, 90f, 1f, false, false, Quadro(42, 1f, 1f, new[] { 0.5f })), false);
    var r8 = Decidir(m, At(q1, 359.8f, 0.75f, false, false, Quadro(42, 1.1f, 1f, new[] { 0.5f })), false);
    Checar(r8.enviar && r8.campos == (byte)(YAW | VIDA), "EstadoEnviado: girou e perdeu vida, vão giro e vida");
    Decidir(m, At(q1, 359.8f, 0.75f, false, false, Quadro(42, 1.2f, 1f, new[] { 0.5f })), false);
    var r9 = Decidir(m, At(q1, 0.2f, 0.75f, false, false, Quadro(42, 1.3f, 1f, new[] { 0.5f })), false);
    var r9b = Decidir(m, At(q1, 2f, 0.75f, false, false, Quadro(42, 1.3f, 1f, new[] { 0.5f })), false);
    Checar(!r9.enviar && r9b.enviar && r9b.campos == YAW, "EstadoEnviado: de 359,8 para 0,2 graus é 0,4 grau pelo caminho curto (não vai); para 2 graus, vai");
    Decidir(m, At(q1, 359f, 0.75f, false, false, Quadro(42, 1.3f, 1f, new[] { 0.5f })), true); // completo: põe o giro em 359
    Decidir(m, At(q1, 359f, 0.75f, false, false, Quadro(42, 1.3f, 1f, new[] { 0.5f })), false); // e a repetição do giro
    var r10 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(42, 1.4f, 1f, new[] { 0.5f })), false);
    Checar(r10.enviar && r10.campos == 0, "EstadoEnviado: só a marca mudou (nasceu), o item vai sem campos");
    var r11 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(42, 1.5f, 1f, new[] { 0.5f })), false);
    var r12 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(42, 1.6f, 1f, new[] { 0.5f })), false);
    Checar(r11.enviar && r11.campos == 0 && !r12.enviar, "EstadoEnviado: a marca é repetida uma vez e depois nada");
    var r13 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.02f, 1f, new[] { 0.5f })), false);
    Checar(r13.enviar && r13.campos == ANIM, "EstadoEnviado: trocou de estado no Animator, vai só a animação");
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.2f, 1f, new[] { 0.5f })), false);
    var r14 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.9f, 1f, new[] { 0.5f })), false);
    var r15 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.05f, 1f, new[] { 0.5f })), false);
    Checar(!r14.enviar && r15.enviar && r15.campos == ANIM, "EstadoEnviado: o mesmo estado recomeçado (o tempo voltou) vai de novo");
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.1f, 1f, new[] { 0.5f })), false);
    var r16 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.2f, 1f, new[] { 0.6f })), false);
    Checar(r16.enviar && r16.campos == ANIM, "EstadoEnviado: parâmetro do Animator mudou 0,1, vai a animação");
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.3f, 1f, new[] { 0.6f })), false);
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.4f, 1f, new[] { 0.015f })), false);
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.5f, 1f, new[] { 0.015f })), false);
    var r17 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.6f, 1f, new[] { 0f })), false);
    Checar(r17.enviar && r17.campos == ANIM, "EstadoEnviado: parâmetro que chega a zero vai mesmo abaixo do mínimo (0,015 → 0)");
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.7f, 1f, new[] { 0f })), false);
    var r18 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.8f, 0.97f, new[] { 0f })), false);
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 0.9f, 0.6f, new[] { 0f })), false);
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.0f, 0.03f, new[] { 0f })), false);
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.1f, 0.03f, new[] { 0f })), false);
    var r19 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.2f, 0f, new[] { 0f })), false);
    Checar(!r18.enviar && r19.enviar && r19.campos == ANIM, "EstadoEnviado: peso de camada 1 → 0,97 não vai; chegar a 0 vai mesmo abaixo do mínimo (0,03 → 0)");
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.3f, 0f, new[] { 0f })), false);
    var r20 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.4f, 0f, new[] { 0f }, new[] { 3 }, new[] { true })), false);
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.5f, 0f, new[] { 0f }, new[] { 3 }, new[] { true })), false);
    var r21 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.6f, 0f, new[] { 0f }, new[] { 3 }, new[] { false })), false);
    Checar(r20.enviar && r20.campos == ANIM && r21.enviar && r21.campos == ANIM, "EstadoEnviado: parâmetros int e bool (quantidade ou valor) mudando levam a animação");
    Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.7f, 0f, new[] { 0f }, new[] { 3 }, new[] { false })), false);
    var r22 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.8f, 0f, new[] { 0f }, new[] { 3 }, new[] { false })), false);
    var r23 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 1.9f, 0f, new[] { 0f }, new[] { 3 }, new[] { false })), true);
    var r24 = Decidir(m, At(q1, 359f, 0.75f, true, false, Quadro(43, 2.0f, 0f, new[] { 0f }, new[] { 3 }, new[] { false })), false);
    Checar(!r22.enviar && r23.enviar && r23.campos == TODOS && !r24.enviar, "EstadoEnviado: o envio completo leva tudo mesmo sem mudança, e não gera repetição");
    var r25 = Decidir(m, At(q1, 359f, 0.75f, true, false, null), true);
    Checar(r25.enviar && r25.campos == (byte)(TODOS & ~ANIM), "EstadoEnviado: completo de inimigo sem Animator vai sem a animação");
    var m2 = Activator.CreateInstance(tEn);
    var r26 = Decidir(m2, At(q0, 0f, 1f, false, false, null), false);
    var r27 = Decidir(m2, At(q0, 0f, 1f, false, false, null), false);
    var r28 = Decidir(m2, At(q0, 0f, 1f, false, false, null), false);
    var r29 = Decidir(m2, At(q0, 0f, 1f, false, false, Quadro(5, 0f, 1f, new float[0])), false);
    Checar(r26.campos == (byte)(TODOS & ~ANIM) && r27.enviar && !r28.enviar && r29.campos == ANIM, "EstadoEnviado: inimigo sem Animator (primeiro envio, repetição, nada) e a primeira animação quando o Animator aparece");
}
{
    var tG = T("HellClockCoop.Net.Golpes"); var tGi = T("HellClockCoop.Net.Golpes+Item");
    var g = Activator.CreateInstance(tG); var lista = (System.Collections.IList)tG.GetField("Itens").GetValue(g);
    var i = Activator.CreateInstance(tGi);
    tGi.GetField("Id").SetValue(i, (ushort)7); tGi.GetField("Valor").SetValue(i, 1234.5f); tGi.GetField("MascaraTipos").SetValue(i, 10); tGi.GetField("Criticos").SetValue(i, (byte)3);
    lista.Add(i);
    var b = (byte[])tG.GetMethod("Serializar").Invoke(g, null);
    var i2 = ((System.Collections.IList)tG.GetField("Itens").GetValue(tG.GetMethod("Desserializar").Invoke(null, new object[] { b })))[0];
    Checar(b[0] == 34 && (ushort)tGi.GetField("Id").GetValue(i2) == 7 && (float)tGi.GetField("Valor").GetValue(i2) == 1234.5f
        && (int)tGi.GetField("MascaraTipos").GetValue(i2) == 10 && (byte)tGi.GetField("Criticos").GetValue(i2) == 3, $"Golpes ida e volta ({b.Length} bytes)");
}
{
    // Status de controle do convidado (0.9.0, tipo 38)
    var tSt = T("HellClockCoop.Net.StatusInimigos"); var tSti = T("HellClockCoop.Net.StatusInimigos+Item");
    var st = Activator.CreateInstance(tSt); var lista = (System.Collections.IList)tSt.GetField("Itens").GetValue(st);
    object item(ushort id, int hash, float dur, float inten, int skill, byte pilhas)
    {
        var i = Activator.CreateInstance(tSti);
        tSti.GetField("Id").SetValue(i, id); tSti.GetField("StatusHash").SetValue(i, hash); tSti.GetField("Duracao").SetValue(i, dur);
        tSti.GetField("Intensidade").SetValue(i, inten); tSti.GetField("SkillHash").SetValue(i, skill); tSti.GetField("Pilhas").SetValue(i, pilhas);
        return i;
    }
    lista.Add(item(7, -123456789, 2.5f, 1.25f, 987654, 3)); lista.Add(item(65535, int.MaxValue, 0.1f, 10f, 0, 255));
    var b = (byte[])tSt.GetMethod("Serializar").Invoke(st, null);
    var l2 = (System.Collections.IList)tSt.GetField("Itens").GetValue(tSt.GetMethod("Desserializar").Invoke(null, new object[] { b }));
    object c(object o, string f) => tSti.GetField(f).GetValue(o);
    Checar(b[0] == 38 && l2.Count == 2 && (ushort)c(l2[0], "Id") == 7 && (int)c(l2[0], "StatusHash") == -123456789 && (float)c(l2[0], "Duracao") == 2.5f
        && (float)c(l2[0], "Intensidade") == 1.25f && (int)c(l2[0], "SkillHash") == 987654 && (byte)c(l2[0], "Pilhas") == 3, $"StatusInimigos tipo=38 ida e volta ({b.Length} bytes)");
    Checar((ushort)c(l2[1], "Id") == 65535 && (int)c(l2[1], "StatusHash") == int.MaxValue && (byte)c(l2[1], "Pilhas") == 255, "StatusInimigos: limites de Id, hash e pilhas");
    var vazio = (byte[])tSt.GetMethod("Serializar").Invoke(Activator.CreateInstance(tSt), null);
    Checar(((System.Collections.IList)tSt.GetField("Itens").GetValue(tSt.GetMethod("Desserializar").Invoke(null, new object[] { vazio }))).Count == 0, "StatusInimigos vazio");
}

// Baús e quebráveis
IdaVolta("BauSpawn", new (string, object)[] { ("Id", (ushort)3), ("Andar", 2u), ("DefinicaoHash", 4455), ("Posicao", new UnityEngine.Vector3(10f, 0.5f, -20f)), ("Rotacao", 135f), ("Aberto", true) }, 40);
IdaVolta("BauAberto", new (string, object)[] { ("Id", (ushort)3) }, 41);
IdaVolta("Quebrou", new (string, object)[] { ("Andar", 2u), ("Posicao", new UnityEngine.Vector3(-1.25f, 0f, 7.5f)) }, 42);
IdaVolta("PortaFumaca", new (string, object)[] {
    ("Andar", 4u), ("Posicao", new UnityEngine.Vector3(12.5f, 0.25f, -3f)), ("Fechada", true), ("Animar", false),
    ("PosicaoHost", new UnityEngine.Vector3(10f, 0f, -1.5f)) }, 45);

// Fase 3b/3c
{
    var t = T("HellClockCoop.Net.FxNovo"); var o = Activator.CreateInstance(t);
    t.GetField("Id").SetValue(o, (ushort)77); t.GetField("Guid").SetValue(o, "0123456789abcdef0123456789abcdef");
    t.GetField("Caminho").SetValue(o, "Pajeu_RIG_360/Hip/Spine_1/Hand_R"); t.GetField("TipoPai").SetValue(o, (byte)2); t.GetField("InimigoId").SetValue(o, (ushort)901);
    t.GetField("Posicao").SetValue(o, new UnityEngine.Vector3(1, 2, 3)); t.GetField("Rotacao").SetValue(o, new UnityEngine.Quaternion(0, 0.7071068f, 0, 0.7071068f));
    t.GetField("Escala").SetValue(o, new UnityEngine.Vector3(2, 2, 2)); t.GetField("LinhaNoMundo").SetValue(o, true);
    t.GetField("Linha").SetValue(o, new[] { new UnityEngine.Vector3(0, 1, 0), new UnityEngine.Vector3(10, 1, 5) });
    var b = (byte[])t.GetMethod("Serializar").Invoke(o, null);
    var fo2 = t.GetMethod("Desserializar").Invoke(null, new object[] { b });
    bool ok = b[0] == 50 && (ushort)t.GetField("Id").GetValue(fo2) == 77 && (string)t.GetField("Guid").GetValue(fo2) == "0123456789abcdef0123456789abcdef"
        && (string)t.GetField("Caminho").GetValue(fo2) == "Pajeu_RIG_360/Hip/Spine_1/Hand_R"
        && (byte)t.GetField("TipoPai").GetValue(fo2) == 2 && (ushort)t.GetField("InimigoId").GetValue(fo2) == 901
        && ((UnityEngine.Quaternion)t.GetField("Rotacao").GetValue(fo2)).Equals(new UnityEngine.Quaternion(0, 0.7071068f, 0, 0.7071068f))
        && ((UnityEngine.Vector3[])t.GetField("Linha").GetValue(fo2)).Length == 2 && (bool)t.GetField("LinhaNoMundo").GetValue(fo2);
    Checar(ok, $"FxNovo ida e volta ({b.Length} bytes)");
}
{
    var t = T("HellClockCoop.Net.FxMovimento"); var ti = T("HellClockCoop.Net.FxMovimento+Item");
    var o = Activator.CreateInstance(t); t.GetField("Tempo").SetValue(o, 12.5f);
    var l = (System.Collections.IList)t.GetField("Itens").GetValue(o);
    var i = Activator.CreateInstance(ti); ti.GetField("Id").SetValue(i, (ushort)5); ti.GetField("Posicao").SetValue(i, new UnityEngine.Vector3(4, 5, 6)); ti.GetField("Rotacao").SetValue(i, UnityEngine.Quaternion.identity);
    l.Add(i);
    var b = (byte[])t.GetMethod("Serializar").Invoke(o, null);
    var fo2 = t.GetMethod("Desserializar").Invoke(null, new object[] { b });
    var i2 = ((System.Collections.IList)t.GetField("Itens").GetValue(fo2))[0];
    Checar(b[0] == 51 && (float)t.GetField("Tempo").GetValue(fo2) == 12.5f && (ushort)ti.GetField("Id").GetValue(i2) == 5
        && ((UnityEngine.Vector3)ti.GetField("Posicao").GetValue(i2)).Equals(new UnityEngine.Vector3(4, 5, 6)), $"FxMovimento ida e volta ({b.Length} bytes)");
}
IdaVolta("FxFim", new (string, object)[] { ("Id", (ushort)77) }, 52);
IdaVolta("InimigoSkill", new (string, object)[] { ("Id", (ushort)300), ("SkillHash", -991), ("TipoAlvo", (byte)2), ("SteamAlvo", 76561198000000001UL), ("PosicaoAlvo", new UnityEngine.Vector3(7, 0, -7)) }, 37);

{
    var t = T("HellClockCoop.Net.DanosVistos"); var ti = T("HellClockCoop.Net.DanosVistos+Item");
    var o = Activator.CreateInstance(t); var l = (System.Collections.IList)t.GetField("Itens").GetValue(o);
    var i = Activator.CreateInstance(ti); ti.GetField("Id").SetValue(i, (ushort)42); ti.GetField("Valor").SetValue(i, 987.5f); ti.GetField("Criticos").SetValue(i, (byte)2); l.Add(i);
    var b = (byte[])t.GetMethod("Serializar").Invoke(o, null);
    var i2 = ((System.Collections.IList)t.GetField("Itens").GetValue(t.GetMethod("Desserializar").Invoke(null, new object[] { b })))[0];
    Checar(b[0] == 53 && (ushort)ti.GetField("Id").GetValue(i2) == 42 && (float)ti.GetField("Valor").GetValue(i2) == 987.5f && (byte)ti.GetField("Criticos").GetValue(i2) == 2, $"DanosVistos ida e volta ({b.Length} bytes)");

    // 0.9.9: o boneco de treino do hub vai com o IdBoneco (0) no meio de inimigos compartilhados.
    ushort idBoneco = (ushort)t.GetField("IdBoneco").GetValue(null);
    var j = Activator.CreateInstance(ti); ti.GetField("Id").SetValue(j, idBoneco); ti.GetField("Valor").SetValue(j, 12.25f); ti.GetField("Criticos").SetValue(j, (byte)0); l.Add(j);
    b = (byte[])t.GetMethod("Serializar").Invoke(o, null);
    var l2 = (System.Collections.IList)t.GetField("Itens").GetValue(t.GetMethod("Desserializar").Invoke(null, new object[] { b }));
    Checar(idBoneco == 0 && l2.Count == 2 && (ushort)ti.GetField("Id").GetValue(l2[0]) == 42 && (ushort)ti.GetField("Id").GetValue(l2[1]) == 0 && (float)ti.GetField("Valor").GetValue(l2[1]) == 12.25f, $"DanosVistos com o boneco de treino ({b.Length} bytes)");
}

{
    var t = T("HellClockCoop.Net.FxNos"); var tn = T("HellClockCoop.Net.FxNos+No");
    var o = Activator.CreateInstance(t); t.GetField("Id").SetValue(o, (ushort)9);
    var l = (System.Collections.IList)t.GetField("Nos").GetValue(o);
    var n = Activator.CreateInstance(tn); tn.GetField("Caminho").SetValue(n, "0/3/1"); tn.GetField("Ativo").SetValue(n, true);
    tn.GetField("Posicao").SetValue(n, new UnityEngine.Vector3(1.8f, 0.8f, 0)); tn.GetField("Rotacao").SetValue(n, UnityEngine.Quaternion.identity); tn.GetField("Escala").SetValue(n, UnityEngine.Vector3.one);
    l.Add(n);
    var b = (byte[])t.GetMethod("Serializar").Invoke(o, null);
    var o2b = t.GetMethod("Desserializar").Invoke(null, new object[] { b });
    var n2 = ((System.Collections.IList)t.GetField("Nos").GetValue(o2b))[0];
    Checar(b[0] == 54 && (ushort)t.GetField("Id").GetValue(o2b) == 9 && (string)tn.GetField("Caminho").GetValue(n2) == "0/3/1" && (bool)tn.GetField("Ativo").GetValue(n2)
        && ((UnityEngine.Vector3)tn.GetField("Posicao").GetValue(n2)).Equals(new UnityEngine.Vector3(1.8f, 0.8f, 0)), $"FxNos ida e volta ({b.Length} bytes)");
}

// Etapa 8 (0.11.0): sons das skills e golpes vistos dos aliados
{
    var t = T("HellClockCoop.Net.SonsAliado"); var ti = T("HellClockCoop.Net.SonsAliado+Item"); var tp = T("HellClockCoop.Net.SonsAliado+Parametro");
    object Item(ushort id, int g1, byte modo, ushort fx, UnityEngine.Vector3 pos, params (string, float)[] ps)
    {
        var i = Activator.CreateInstance(ti);
        ti.GetField("Id").SetValue(i, id); ti.GetField("G1").SetValue(i, g1); ti.GetField("G2").SetValue(i, -2); ti.GetField("G3").SetValue(i, 3); ti.GetField("G4").SetValue(i, int.MinValue);
        ti.GetField("Modo").SetValue(i, modo); ti.GetField("FxId").SetValue(i, fx); ti.GetField("Posicao").SetValue(i, pos);
        var lp = (System.Collections.IList)ti.GetField("Parametros").GetValue(i);
        foreach (var (n, v) in ps) { var p = Activator.CreateInstance(tp); tp.GetField("Nome").SetValue(p, n); tp.GetField("Valor").SetValue(p, v); lp.Add(p); }
        return i;
    }
    object Ler(byte[] b) => t.GetMethod("Desserializar").Invoke(null, new object[] { b });
    System.Collections.IList Itens(object o) => (System.Collections.IList)t.GetField("Itens").GetValue(o);
    byte[] Ser(object o) => (byte[])t.GetMethod("Serializar").Invoke(o, null);

    var o = Activator.CreateInstance(t);
    Itens(o).Add(Item(0, 123456789, 0, 0, new UnityEngine.Vector3(1, 2, 3), ("Intensity", 0.5f), ("Surface", 2f)));
    Itens(o).Add(Item(77, -5, 2, 300, new UnityEngine.Vector3(-4, 0, 9)));
    var b = Ser(o);
    var l2 = Itens(Ler(b));
    var a = l2[0]; var c = l2[1];
    var ps2 = (System.Collections.IList)ti.GetField("Parametros").GetValue(a);
    Checar(b[0] == 55 && l2.Count == 2 && (int)ti.GetField("G1").GetValue(a) == 123456789 && (int)ti.GetField("G2").GetValue(a) == -2 && (int)ti.GetField("G4").GetValue(a) == int.MinValue
        && ps2.Count == 2 && (string)tp.GetField("Nome").GetValue(ps2[1]) == "Surface" && (float)tp.GetField("Valor").GetValue(ps2[0]) == 0.5f
        && (ushort)ti.GetField("Id").GetValue(c) == 77 && (byte)ti.GetField("Modo").GetValue(c) == 2 && (ushort)ti.GetField("FxId").GetValue(c) == 300
        && ((UnityEngine.Vector3)ti.GetField("Posicao").GetValue(c)).Equals(new UnityEngine.Vector3(-4, 0, 9)), $"SonsAliado ida e volta ({b.Length} bytes)");

    bool Recusa(byte[] bytes) { try { Ler(bytes); return false; } catch (TargetInvocationException e) when (e.InnerException is FormatException) { return true; } }
    var om = Activator.CreateInstance(t); Itens(om).Add(Item(0, 1, 3, 0, UnityEngine.Vector3.zero));
    Checar(Recusa(Ser(om)), "SonsAliado: modo de som desconhecido (3) derruba a mensagem");
    var muitos = Activator.CreateInstance(t); for (int k = 0; k < 70; k++) Itens(muitos).Add(Item(0, k + 1, 0, 0, UnityEngine.Vector3.zero));
    Checar(Itens(Ler(Ser(muitos))).Count == 64, "SonsAliado: Serializar corta em 64 itens");
    Checar(Recusa(new byte[] { 55, 65 }), "SonsAliado: 65 itens no cabeçalho derrubam a mensagem");
    var mp = Activator.CreateInstance(t); Itens(mp).Add(Item(0, 1, 0, 0, UnityEngine.Vector3.zero, ("a", 1f), ("b", 2f), ("c", 3f), ("d", 4f), ("e", 5f), ("f", 6f)));
    Checar(((System.Collections.IList)ti.GetField("Parametros").GetValue(Itens(Ler(Ser(mp)))[0])).Count == 4, "SonsAliado: Serializar corta em 4 parâmetros");
    // cabeçalho de 1 item com 5 parâmetros, escrito à mão: Id(2) G(16) Modo(1) FxId(2) Pos(12) nParams=5
    var bp = new byte[1 + 1 + 2 + 16 + 1 + 2 + 12 + 1]; bp[0] = 55; bp[1] = 1; bp[bp.Length - 1] = 5;
    Checar(Recusa(bp), "SonsAliado: 5 parâmetros num som derrubam a mensagem");

    // Texto com tamanho declarado falso (2 GB num pacote de poucos bytes) é recusado antes de alocar (0.11.0, todos os codecs).
    var bt = new byte[1 + 1 + 2 + 16 + 1 + 2 + 12 + 1 + 5]; bt[0] = 55; bt[1] = 1; bt[35] = 1;
    bt[36] = 0xFF; bt[37] = 0xFF; bt[38] = 0xFF; bt[39] = 0xFF; bt[40] = 0x07;
    Checar(Recusa(bt), "SonsAliado: nome de parâmetro com tamanho declarado de 2 GB recusado sem alocar");
    var tHelloT = T("HellClockCoop.Net.Hello");
    var hb = new byte[] { 10, 5, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0x07 };
    bool helloRecusa; try { tHelloT.GetMethod("Desserializar").Invoke(null, new object[] { hb }); helloRecusa = false; } catch (TargetInvocationException e) when (e.InnerException is FormatException) { helloRecusa = true; }
    Checar(helloRecusa, "Hello: versão com tamanho declarado de 2 GB recusada sem alocar");

    var tf = T("HellClockCoop.Net.SonsFim"); var f = Activator.CreateInstance(tf);
    var ids = (System.Collections.Generic.List<ushort>)tf.GetField("Ids").GetValue(f); ids.Add(1); ids.Add(65535);
    var bf = (byte[])tf.GetMethod("Serializar").Invoke(f, null);
    var ids2 = (System.Collections.Generic.List<ushort>)tf.GetField("Ids").GetValue(tf.GetMethod("Desserializar").Invoke(null, new object[] { bf }));
    Checar(bf[0] == 56 && ids2.Count == 2 && ids2[0] == 1 && ids2[1] == 65535, $"SonsFim ida e volta ({bf.Length} bytes)");

    var tg = T("HellClockCoop.Net.GolpesVistos"); var tgi = T("HellClockCoop.Net.GolpesVistos+Item");
    var g = Activator.CreateInstance(tg); var gl = (System.Collections.IList)tg.GetField("Itens").GetValue(g);
    var gi = Activator.CreateInstance(tgi);
    tgi.GetField("Id").SetValue(gi, (ushort)42); tgi.GetField("Opcoes").SetValue(gi, (byte)7); tgi.GetField("Tipos").SetValue(gi, (byte)0b1010); tgi.GetField("Principal").SetValue(gi, (byte)3);
    tgi.GetField("Critico").SetValue(gi, true); tgi.GetField("Direcao").SetValue(gi, new UnityEngine.Vector3(0, 0, -1)); tgi.GetField("Deslocamento").SetValue(gi, new UnityEngine.Vector3(0.2f, 1.1f, 0));
    gl.Add(gi);
    var bg = (byte[])tg.GetMethod("Serializar").Invoke(g, null);
    var gi2 = ((System.Collections.IList)tg.GetField("Itens").GetValue(tg.GetMethod("Desserializar").Invoke(null, new object[] { bg })))[0];
    Checar(bg[0] == 57 && (ushort)tgi.GetField("Id").GetValue(gi2) == 42 && (byte)tgi.GetField("Opcoes").GetValue(gi2) == 7 && (byte)tgi.GetField("Tipos").GetValue(gi2) == 0b1010
        && (byte)tgi.GetField("Principal").GetValue(gi2) == 3 && (bool)tgi.GetField("Critico").GetValue(gi2)
        && ((UnityEngine.Vector3)tgi.GetField("Deslocamento").GetValue(gi2)).Equals(new UnityEngine.Vector3(0.2f, 1.1f, 0)), $"GolpesVistos ida e volta ({bg.Length} bytes)");

    var tv = T("HellClockCoop.Net.Validacao");
    bool V(string m, params object[] args) => (bool)tv.GetMethod(m).Invoke(null, args);
    Checar(V("SomValido", 1, 0, 0, 0, 39f, 0f, 0f, 0f, 0f, 0f) && V("SomValido", 0, 0, 0, -1, 10f, 10f, 10f, 10f, 10f, 10f), "Validação: som com GUID e a até 40 m do avatar aceito");
    Checar(!V("SomValido", 0, 0, 0, 0, 0f, 0f, 0f, 0f, 0f, 0f), "Validação: som com GUID nulo recusado");
    Checar(!V("SomValido", 1, 0, 0, 0, 40.5f, 0f, 0f, 0f, 0f, 0f) && !V("SomValido", 1, 0, 0, 0, float.NaN, 0f, 0f, 0f, 0f, 0f) && !V("SomValido", 1, 0, 0, 0, 0f, float.PositiveInfinity, 0f, 0f, 0f, 0f),
        "Validação: som a 40,5 m do avatar, NaN ou ∞ recusado");
    Checar(V("ParametroSomValido", "Intensity", 0.5f) && !V("ParametroSomValido", "", 1f) && !V("ParametroSomValido", null, 1f) && !V("ParametroSomValido", "x", float.NaN)
        && !V("ParametroSomValido", new string('a', 65), 1f), "Validação: parâmetro de som com nome vazio, nome de 65 letras ou valor NaN recusado");
    {
        var tj = T("HellClockCoop.Net.JanelaBonus"); var j = Activator.CreateInstance(tj); tj.GetField("Inicio").SetValue(j, 0f);
        int aceitos = 0; for (int k = 0; k < 100; k++) if (V("AceitarSomNaJanela", j, 0.5f + k * 0.001f)) aceitos++;
        Checar(aceitos == 40, $"Validação: 100 sons em 0,1 s de um aliado → só 40 aceitos ({aceitos})");
        Checar(V("AceitarSomNaJanela", j, 1.5f), "Validação: a janela de sons recomeça depois de 1 s");
        var j2 = Activator.CreateInstance(tj); tj.GetField("Inicio").SetValue(j2, 0f);
        int golpes = 0; for (int k = 0; k < 300; k++) if (V("AceitarNaJanela", j2, 0.1f, (int)tv.GetField("GolpesVistosPorJanelaMaximo").GetValue(null))) golpes++;
        Checar(golpes == 120, $"Validação: 300 golpes vistos no mesmo instante → só 120 aceitos ({golpes})");
    }
    bool GV(byte op, byte tipos, byte pr, float ox = 0f, float dx = 0f) => V("GolpeVistoValido", op, tipos, pr, dx, 0f, 1f, ox, 0f, 0f);
    Checar(GV(7, 0b1010, 3) && GV(1, 1, 0, 9.9f), "Validação: golpe visto com opções e tipos conhecidos e principal presente aceito");
    Checar(!GV(8, 1, 0) && !GV(1, 0, 0) && !GV(1, 0b10000, 4) && !GV(1, 0b0010, 0) && !GV(1, 1, 4), "Validação: golpe visto com opção ou tipo desconhecido, sem tipo ou principal ausente recusado");
    Checar(!GV(1, 1, 0, 10.5f) && !GV(1, 1, 0, 0f, float.NaN), "Validação: golpe visto a 10,5 m do inimigo ou com direção NaN recusado");
}

// Santuários e totens
IdaVolta("ObjetoSpawn", new (string, object)[] { ("Id", (ushort)12), ("Andar", 3u), ("Tipo", (byte)1), ("DefinicaoHash", 777), ("Posicao", new UnityEngine.Vector3(5f, 0f, 9f)), ("Raio", 2.5f), ("Estado", (byte)2) }, 43);
IdaVolta("ObjetoUsado", new (string, object)[] { ("Id", (ushort)12), ("Estado", (byte)1) }, 44);

// Validação na recepção (0.8.4): a mensagem passa pelo codec e o valor lido de volta passa pelo filtro.
{
    var tv = T("HellClockCoop.Net.Validacao");
    bool V(string m, params object[] a) => (bool)tv.GetMethod(m).Invoke(null, a);
    object Lido(string tipo, (string campo, object valor)[] campos)
    {
        var t = T("HellClockCoop.Net." + tipo); var o = Activator.CreateInstance(t);
        foreach (var (c, v) in campos) t.GetField(c).SetValue(o, v);
        return t.GetMethod("Desserializar").Invoke(null, new object[] { (byte[])t.GetMethod("Serializar").Invoke(o, null) });
    }
    object Campo(object o, string c) => o.GetType().GetField(c).GetValue(o);

    // PlanoAndar: MaxLevelIndex é a quantidade de andares (índices 0..Max-1); andar especial (sem dungeon = -1): teto de sanidade
    uint andar = (uint)Campo(Lido("PlanoAndar", new (string, object)[] { ("IndiceAndar", 10u) }), "IndiceAndar");
    Checar(V("AndarValido", andar, 11) && !V("AndarValido", andar + 1, 11) && !V("AndarValido", 0u, 0), "Validação: andar 10 aceito com MaxLevelIndex 11; 11 (um além do fim) recusado");
    Checar(V("AndarValido", 200u, -1) && !V("AndarValido", 201u, -1) && !V("AndarValido", uint.MaxValue, -1), "Validação: andar especial até o teto de sanidade (200)");
    Checar(!V("AndarValido", uint.MaxValue, 30), "Validação: andar uint.MaxValue recusado");

    // FimRun: só EGameOverSource (0..4)
    int mot(int m) => (int)Campo(Lido("FimRun", new (string, object)[] { ("Motivo", m) }), "Motivo");
    Checar(V("MotivoFimRunValido", mot(0)) && V("MotivoFimRunValido", mot(4)), "Validação: FimRun motivo 0 e 4 aceitos");
    Checar(!V("MotivoFimRunValido", mot(5)) && !V("MotivoFimRunValido", mot(-1)) && !V("MotivoFimRunValido", mot(int.MaxValue)), "Validação: FimRun motivo 5, -1 e int.MaxValue recusados");

    // PedidoAvanco (0.15.0): só do andar atual do host, pelo menos 1 andar (o host limita a 3), nunca no último andar
    Checar(V("PedidoAvancoValido", 6u, 1u, 6u, false) && V("PedidoAvancoValido", 6u, 5u, 6u, false), "Validação: PedidoAvanco do andar do host com 1 e 5 andares aceito (o host limita a 3)");
    Checar(!V("PedidoAvancoValido", 5u, 1u, 6u, false) && !V("PedidoAvancoValido", 7u, 1u, 6u, false), "Validação: PedidoAvanco de outro andar (atrasado ou adiantado) recusado");
    Checar(!V("PedidoAvancoValido", 6u, 0u, 6u, false), "Validação: PedidoAvanco com 0 andares recusado");
    Checar(!V("PedidoAvancoValido", 6u, 1u, 6u, true), "Validação: PedidoAvanco no último andar recusado (viraria dungeon concluída no save do host)");
    Checar((uint)tv.GetField("AvancoMaximo").GetValue(null) == 3u, "Validação: teto de 3 andares por pedido");

    // Reanimando (0.16.0): andar atual, estado 0..2, fração finita de 0 a 1
    Checar(V("ReanimandoValido", 4u, (byte)0, 0f, 4u) && V("ReanimandoValido", 4u, (byte)2, 1f, 4u) && V("ReanimandoValido", 4u, (byte)1, 0.5f, 4u), "Validação: Reanimando do andar atual com estado 0, 1 e 2 aceito");
    Checar(!V("ReanimandoValido", 3u, (byte)0, 0.5f, 4u), "Validação: Reanimando de outro andar recusado");
    Checar(!V("ReanimandoValido", 4u, (byte)3, 0.5f, 4u) && !V("ReanimandoValido", 4u, (byte)255, 0.5f, 4u), "Validação: Reanimando com estado 3 e 255 recusado");
    Checar(!V("ReanimandoValido", 4u, (byte)0, float.NaN, 4u) && !V("ReanimandoValido", 4u, (byte)0, float.PositiveInfinity, 4u)
        && !V("ReanimandoValido", 4u, (byte)0, -0.1f, 4u) && !V("ReanimandoValido", 4u, (byte)0, 1.01f, 4u), "Validação: Reanimando com fração NaN, ∞, negativa e acima de 1 recusado");

    // Reanimou (0.16.0): andar do caído e quem reanimou a até raio + folga (2 + 2 m)
    Checar(V("ReanimouValido", 4u, 4u, 0f, 2f) && V("ReanimouValido", 4u, 4u, 4f, 2f), "Validação: Reanimou do andar com quem reanimou a 0 e 4 m (raio 2 + folga 2) aceito");
    Checar(!V("ReanimouValido", 4u, 4u, 4.5f, 2f) && !V("ReanimouValido", 4u, 4u, 3.4e38f, 2f), "Validação: Reanimou de longe (4,5 m) ou sem posição de quem reanimou recusado");
    Checar(!V("ReanimouValido", 3u, 4u, 1f, 2f) && !V("ReanimouValido", 4u, 4u, float.NaN, 2f) && !V("ReanimouValido", 4u, 4u, -1f, 2f), "Validação: Reanimou de outro andar, com distância NaN ou negativa recusado");

    // Ordem da reanimação (0.16.0): fica o maior valor, subindo no máximo um por mensagem
    int ordem(byte o, int conta) => (int)tv.GetMethod("OrdemReanimacao").Invoke(null, new object[] { o, conta });
    Checar(ordem(1, 0) == 1 && ordem(3, 2) == 3, "Validação: Reanimou com a ordem seguinte sobe a conta do andar");
    Checar(ordem(1, 1) == 1 && ordem(1, 3) == 3 && ordem(0, 2) == 2, "Validação: Reanimou repetido, atrasado ou com ordem 0 não muda a conta");
    Checar(ordem(255, 0) == 1 && ordem(9, 2) == 3, "Validação: Reanimou com ordem forjada alta sobe só um");

    // Marcador (0.17.0): mesma cena e andar, ponto finito, até alcance + folga (30 + 10 m) de quem marcou e até 10 m de desnível
    bool mvd(int cena, uint andar, float x, float y, float z, int cenaLocal, uint andarLocal, float d, float dy) => V("MarcadorValido", cena, andar, x, y, z, cenaLocal, andarLocal, d, dy);
    bool mv(int cena, uint andar, float x, float y, float z, int cenaLocal, uint andarLocal, float d) => mvd(cena, andar, x, y, z, cenaLocal, andarLocal, d, 0.5f);
    Checar(mvd(77, 3u, 1f, 0f, 1f, 77, 3u, 1f, 0f) && mvd(77, 3u, 1f, 0f, 1f, 77, 3u, 1f, 10f), "Validação: Marcador com desnível de 0 e 10 m aceito");
    Checar(!mvd(77, 3u, 1f, 0f, 1f, 77, 3u, 1f, 10.5f) && !mvd(77, 3u, 1f, 0f, 1f, 77, 3u, 1f, 3.4e38f) && !mvd(77, 3u, 1f, 0f, 1f, 77, 3u, 1f, float.NaN) && !mvd(77, 3u, 1f, 0f, 1f, 77, 3u, 1f, -1f),
        "Validação: Marcador no alto (10,5 m acima), sem posição de quem marcou, com desnível NaN ou negativo recusado");
    Checar(mv(77, 3u, 10f, 0.5f, -20f, 77, 3u, 0f) && mv(77, 3u, 0f, 0f, 0f, 77, 3u, 40f) && mv(77, 4294967295u, 1f, 0f, 1f, 77, 4294967295u, 5f),
        "Validação: Marcador da mesma cena e andar (também fora da run) a 0, 5 e 40 m de quem marcou aceito");
    Checar(!mv(77, 3u, 1f, 0f, 1f, 78, 3u, 1f) && !mv(77, 3u, 1f, 0f, 1f, 77, 4u, 1f) && !mv(-1, 3u, 1f, 0f, 1f, -1, 3u, 1f),
        "Validação: Marcador de outra cena, de outro andar ou com cena desconhecida (-1) recusado");
    Checar(!mv(77, 3u, float.NaN, 0f, 1f, 77, 3u, 1f) && !mv(77, 3u, 1f, float.PositiveInfinity, 1f, 77, 3u, 1f) && !mv(77, 3u, 1f, 0f, 100001f, 77, 3u, 1f),
        "Validação: Marcador com coordenada NaN, ∞ ou acima de 100 000 recusado");
    Checar(!mv(77, 3u, 1f, 0f, 1f, 77, 3u, 40.5f) && !mv(77, 3u, 1f, 0f, 1f, 77, 3u, 3.4e38f) && !mv(77, 3u, 1f, 0f, 1f, 77, 3u, float.NaN) && !mv(77, 3u, 1f, 0f, 1f, 77, 3u, -1f),
        "Validação: Marcador longe de quem marcou (40,5 m), sem posição dele, com distância NaN ou negativa recusado");

    // Teleporte até o aliado (0.29.0): mesmo andar, os dois de pé, 5 s fora de combate, recarga de 60 s (folga de 1 s)
    byte mtp(uint ap, uint ah, bool pn, bool pp, bool an, bool av, float c, float u) =>
        (byte)tv.GetMethod("MotivoTp").Invoke(null, new object[] { ap, ah, pn, pp, an, av, c, u });
    Checar(mtp(4u, 4u, true, true, true, true, 5f, 59f) == 0 && mtp(4u, 4u, true, true, true, true, 300f, 1e9f) == 0,
        "Validação: TP no mesmo andar, de pé, 5 s sem combate e 59 s desde o último (folga de 1 s) autorizado");
    Checar(mtp(3u, 4u, true, true, true, true, 9f, 99f) == 1 && mtp(4u, 4u, false, true, true, true, 9f, 99f) == 1,
        "Validação: TP de outro andar (pedido ou quem pede fora do andar do host) recusado com OutroAndar");
    Checar(mtp(4u, 4u, true, false, true, true, 9f, 99f) == 2, "Validação: TP de quem está caído recusado com VoceCaido");
    Checar(mtp(4u, 4u, true, true, false, true, 9f, 99f) == 3 && mtp(4u, 4u, true, true, true, false, 9f, 99f) == 3,
        "Validação: TP até aliado fora do andar ou caído recusado com AlvoIndisponivel");
    Checar(mtp(4u, 4u, true, true, true, true, 4.9f, 99f) == 4 && mtp(4u, 4u, true, true, true, true, float.NaN, 99f) == 4
        && mtp(4u, 4u, true, true, true, true, -1f, 99f) == 4, "Validação: TP com combate há 4,9 s, NaN ou negativo recusado com EmCombate");
    Checar(mtp(4u, 4u, true, true, true, true, 9f, 58.9f) == 5 && mtp(4u, 4u, true, true, true, true, 9f, float.NaN) == 5
        && mtp(4u, 4u, true, true, true, true, 9f, float.PositiveInfinity) == 5, "Validação: TP em recarga (58,9 s), NaN ou ∞ recusado com Recarga");
    float rtp(string m, float v) => (float)tv.GetMethod(m).Invoke(null, new object[] { v });
    Checar(rtp("RecargaTpRestante", 18f) == 42f && rtp("RecargaTpRestante", 90f) == 0f && rtp("RecargaTpRestante", float.NaN) == 60f && rtp("RecargaTpRestante", -1f) == 60f,
        "Validação: recarga do TP que falta (18 s → 42 s; 90 s → 0; NaN/negativo → 60)");
    Checar(rtp("RecargaTpRecebida", 42f) == 42f && rtp("RecargaTpRecebida", 1e9f) == 60f && rtp("RecargaTpRecebida", float.NaN) == 0f && rtp("RecargaTpRecebida", -3f) == 0f,
        "Validação: recarga do TP recebida do host presa entre 0 e 60 s (NaN/negativa = 0)");

    // Sequência do snapshot para Morto (0.16.0): atrasado até 64 é ignorado; recomeço da contagem vale
    bool atr(ushort s, int u) => (bool)tv.GetMethod("SequenciaAtrasada").Invoke(null, new object[] { s, u });
    Checar(!atr(5, -1) && !atr(11, 10) && !atr(3, 65535), "Validação: snapshot novo (primeiro, seguinte e virada de 65535 para 3) muda Morto");
    Checar(atr(10, 10) && atr(9, 10) && atr(65530, 2), "Validação: snapshot repetido ou um pouco atrasado (também na virada) não muda Morto");
    Checar(!atr(0, 5000), "Validação: snapshot muito atrás (o outro PC recomeçou a contagem) muda Morto");

    // TimerBonus: finito, > 0 e até 120 s
    float bonus(float s) => (float)Campo(Lido("TimerBonus", new (string, object)[] { ("Segundos", s) }), "Segundos");
    Checar(V("BonusTempoValido", bonus(15f)) && V("BonusTempoValido", bonus(120f)), "Validação: TimerBonus 15 s e 120 s aceitos");

    Checar(!V("BonusTempoValido", bonus(float.NaN)) && !V("BonusTempoValido", bonus(float.PositiveInfinity)) && !V("BonusTempoValido", bonus(0f))
        && !V("BonusTempoValido", bonus(-5f)) && !V("BonusTempoValido", bonus(120.5f)) && !V("BonusTempoValido", bonus(3.4e38f)), "Validação: TimerBonus NaN, ∞, 0, negativo, 120,5 s e 3.4e38 recusados");

    // TimerBonus acumulado (0.8.5): até 120 s por convidado numa janela de 60 s
    {
        var tj = T("HellClockCoop.Net.JanelaBonus"); var j = Activator.CreateInstance(tj); tj.GetField("Inicio").SetValue(j, 10f);
        bool Aceita(float agora, float s) => V("AceitarBonusNaJanela", j, agora, bonus(s));
        Checar(Aceita(10f, 60f) && Aceita(30f, 60f) && !Aceita(40f, 1f), "Validação: bônus 60 s + 60 s aceitos; o seguinte na mesma janela de 60 s recusado");
        Checar((float)tj.GetField("Soma").GetValue(j) == 120f, "Validação: bônus recusado não entra na soma da janela");
        Checar(Aceita(70f, 120f) && !Aceita(71f, 0.5f), "Validação: janela recomeça após 60 s (120 s aceitos, o resto recusado)");
        Checar(Aceita(5f, 10f), "Validação: relógio que volta recomeça a janela");
        int aceitos = 0; for (int k = 0; k < 1000; k++) if (Aceita(200f + k * 0.01f, 120f)) aceitos++;
        Checar(aceitos == 1, $"Validação: 1000 bônus de 120 s em 10 s → só 1 aceito ({aceitos})");
        int miudos = 0; for (int k = 0; k < 500; k++) if (Aceita(300f + k * 0.01f, 0.001f)) miudos++;
        Checar(miudos == 120, $"Validação: 500 bônus de 0,001 s na janela → só 120 aceitos, o log não enche ({miudos})");
        Checar(!Aceita(400f, float.NaN) && !Aceita(400.1f, 0f) && Aceita(400.2f, 5f), "Validação: bônus inválido passa pela janela, é recusado e não conta");
        Checar(Aceita(500f, 120f) && !Aceita(559.99f, 1f) && Aceita(560f, 120f), "Validação: borda da janela — em 59,99 s ainda recusa; em 60 s exatos recomeça");
    }

    // Golpes: dano finito e > 0 (NaN/∞ corromperia a vida do inimigo no host; sem teto de valor)
    {
        var tG2 = T("HellClockCoop.Net.Golpes"); var tGi2 = T("HellClockCoop.Net.Golpes+Item");
        float lidoGolpe(float v)
        {
            var g = Activator.CreateInstance(tG2); var it = Activator.CreateInstance(tGi2);
            tGi2.GetField("Id").SetValue(it, (ushort)1); tGi2.GetField("Valor").SetValue(it, v);
            ((System.Collections.IList)tG2.GetField("Itens").GetValue(g)).Add(it);
            var g2 = tG2.GetMethod("Desserializar").Invoke(null, new object[] { (byte[])tG2.GetMethod("Serializar").Invoke(g, null) });
            return (float)tGi2.GetField("Valor").GetValue(((System.Collections.IList)tG2.GetField("Itens").GetValue(g2))[0]);
        }
        Checar(V("DanoValido", lidoGolpe(1234.5f)) && V("DanoValido", lidoGolpe(3.4e38f)), "Validação: golpe 1234,5 e valor finito grande aceitos (dano do convidado é confiável)");
        Checar(!V("DanoValido", lidoGolpe(float.NaN)) && !V("DanoValido", lidoGolpe(float.PositiveInfinity)) && !V("DanoValido", lidoGolpe(0f)) && !V("DanoValido", lidoGolpe(-3f)),
            "Validação: golpe NaN, ∞, 0 e negativo recusados");
    }

    // TimerSync: os dois campos finitos e >= 0
    object timer(float a, float b) => Lido("TimerSync", new (string, object)[] { ("Timer", a), ("TimerRelaxado", b) });
    Checar(V("TimerValido", timer(0f, 0f)) && V("TimerValido", timer(417.25f, 999.5f)), "Validação: TimerSync 0 e valores normais aceitos");
    Checar(!V("TimerValido", timer(float.NaN, 1f)) && !V("TimerValido", timer(1f, float.NegativeInfinity)) && !V("TimerValido", timer(-0.5f, 1f))
        && !V("TimerValido", timer(1f, -1f)) && !V("TimerValido", new object[] { null }), "Validação: TimerSync NaN, -∞, negativo e nulo recusados");

    // ObjetoUsado: Concluido (purificação) só do host e, desde a 0.20.0, depois do santuário ativado; Usado de qualquer par
    byte est(byte e) => (byte)Campo(Lido("ObjetoUsado", new (string, object)[] { ("Id", (ushort)12), ("Estado", e) }), "Estado");
    Checar(V("ObjetoUsadoAceito", est(2), true, true) && !V("ObjetoUsadoAceito", est(2), false, true), "Validação: ObjetoUsado Concluido só do host");
    Checar(!V("ObjetoUsadoAceito", est(2), true, false), "Validação: ObjetoUsado Concluido antes do santuário ser ativado recusado (0.20.0)");
    Checar(V("ObjetoUsadoAceito", est(1), true, false) && V("ObjetoUsadoAceito", est(1), false, false), "Validação: ObjetoUsado Usado de qualquer par");

    // 0.20.0 — segurança com desconhecidos
    {
        // FimRun: Completed só no último andar de uma run que acompanha o host; senão ManualFinish
        int fim(int m, bool ultimo, bool seguindo) => (int)tv.GetMethod("MotivoFimRunAplicado").Invoke(null, new object[] { mot(m), ultimo, seguindo });
        Checar(fim(3, true, true) == 3, "Validação: FimRun Completed no último andar acompanhando o host vale");
        Checar(fim(3, false, true) == 1 && fim(3, true, false) == 1 && fim(3, false, false) == 1, "Validação: FimRun Completed fora do último andar ou em run própria vira ManualFinish");
        Checar(fim(0, false, true) == 0 && fim(1, false, false) == 1 && fim(2, false, true) == 2 && fim(4, false, true) == 4, "Validação: FimRun Defeat, ManualFinish, Timer e portal passam iguais");

        // PlanoAndar com a run aberta: mesma run e salto até 3 andares
        int planoHash(int h) => (int)Campo(Lido("PlanoAndar", new (string, object)[] { ("DungeonHash", h) }), "DungeonHash");
        Checar(V("PlanoDaRunAberta", planoHash(111), 0, 111, 0) && V("PlanoDaRunAberta", 0, 2, 0, 2) && V("PlanoDaRunAberta", 999, 1, 5, 1),
            "Validação: PlanoAndar da mesma masmorra ou do mesmo atalho especial aceito");
        Checar(V("PlanoDaRunAberta", 111, 0, 111, 2), "Validação: PlanoAndar comum da mesma masmorra saindo de um atalho especial aceito");
        Checar(!V("PlanoDaRunAberta", planoHash(222), 0, 111, 0) && !V("PlanoDaRunAberta", 111, 1, 111, 0) && !V("PlanoDaRunAberta", 111, 1, 111, 2) && !V("PlanoDaRunAberta", 222, 0, 111, 2),
            "Validação: PlanoAndar de outra masmorra, de atalho com a run comum aberta ou de outro atalho recusado");
        uint salto(uint p, uint a) => (uint)tv.GetMethod("SaltoPlano").Invoke(null, new object[] { p, a });
        Checar(salto(5, 4) == 1 && salto(7, 4) == 3 && salto(4, 4) == 0 && salto(2, 4) == 0, "Validação: salto do plano 1, 3, 0 (mesmo andar) e 0 (atrás)");
        Checar(salto(4294967295u, 0) == 3 && salto(200, 1) == 3, "Validação: salto do plano forjado longe limitado a 3");

        // InimigoSpawn e InimigoAjuste
        bool isp(byte tipo, int afixos, byte aj, float xp, float vida, float atraso, float yaw, float x, float ox) =>
            V("InimigoSpawnValido", tipo, afixos, aj, xp, vida, atraso, yaw, x, 0f, 1f, ox, 0f, 1f);
        Checar(isp(0, 0, 0, 1f, 1f, 0f, 0f, 10f, 10f) && isp(6, 32, 15, 10f, 0f, 30f, 359f, -99999f, 0f) && isp(3, 5, 4, 0f, 0.5f, 1.5f, -90f, 0f, 0f),
            "Validação: InimigoSpawn com tipo 0..6, até 32 afixos, XP 0..10, vida 0..1 e atraso 0..30 s aceito");
        Checar(!isp(7, 0, 0, 1f, 1f, 0f, 0f, 0f, 0f) && !isp(255, 0, 0, 1f, 1f, 0f, 0f, 0f, 0f), "Validação: InimigoSpawn com tipo fora do EEnemyType recusado");
        Checar(!isp(0, 33, 0, 1f, 1f, 0f, 0f, 0f, 0f) && !isp(0, 255, 0, 1f, 1f, 0f, 0f, 0f, 0f), "Validação: InimigoSpawn com mais de 32 afixos recusado");
        Checar(!isp(0, 0, 4, 10.5f, 1f, 0f, 0f, 0f, 0f) && !isp(0, 0, 4, float.NaN, 1f, 0f, 0f, 0f, 0f) && !isp(0, 0, 4, -1f, 1f, 0f, 0f, 0f, 0f) && !isp(0, 0, 4, 3.4e38f, 1f, 0f, 0f, 0f, 0f),
            "Validação: InimigoSpawn com XP 10,5, NaN, negativo ou 3.4e38 recusado");
        Checar(isp(0, 0, 0, float.NaN, 1f, 0f, 0f, 0f, 0f), "Validação: InimigoSpawn sem a marca Xp ignora o multiplicador");
        Checar(!isp(0, 0, 16, 1f, 1f, 0f, 0f, 0f, 0f) && !isp(0, 0, 255, 1f, 1f, 0f, 0f, 0f, 0f), "Validação: InimigoSpawn com marca de ajuste desconhecida recusado");
        Checar(!isp(0, 0, 0, 1f, 1.01f, 0f, 0f, 0f, 0f) && !isp(0, 0, 0, 1f, -0.1f, 0f, 0f, 0f, 0f) && !isp(0, 0, 0, 1f, float.NaN, 0f, 0f, 0f, 0f),
            "Validação: InimigoSpawn com vida acima de 1, negativa ou NaN recusado");
        Checar(!isp(0, 0, 0, 1f, 1f, 30.5f, 0f, 0f, 0f) && !isp(0, 0, 0, 1f, 1f, -1f, 0f, 0f, 0f) && !isp(0, 0, 0, 1f, 1f, float.PositiveInfinity, 0f, 0f, 0f),
            "Validação: InimigoSpawn com atraso acima de 30 s, negativo ou ∞ recusado");
        Checar(!isp(0, 0, 0, 1f, 1f, 0f, float.NaN, 0f, 0f) && !isp(0, 0, 0, 1f, 1f, 0f, 0f, float.NaN, 0f) && !isp(0, 0, 0, 1f, 1f, 0f, 0f, 100001f, 0f) && !isp(0, 0, 0, 1f, 1f, 0f, 0f, 0f, float.NegativeInfinity),
            "Validação: InimigoSpawn com giro NaN, posição NaN ou acima de 100 000, ou origem -∞ recusado");
        Checar(V("AjusteValido", (byte)4, 2.5f) && V("AjusteValido", (byte)9, float.NaN) && !V("AjusteValido", (byte)4, 11f) && !V("AjusteValido", (byte)32, 1f),
            "Validação: InimigoAjuste com XP no teto aceito; XP 11 ou marca desconhecida recusado");

        // ObjetoSpawn e teto por andar
        bool osp(byte tipo, float raio, float x) => V("ObjetoSpawnValido", tipo, raio, x, 0f, 0f);
        Checar(osp(1, 1f, 5f) && osp(2, 200f, -5f), "Validação: ObjetoSpawn santuário e totem com raio até 200 aceito");
        Checar(!osp(0, 1f, 0f) && !osp(3, 1f, 0f) && !osp(1, 0f, 0f) && !osp(1, -1f, 0f) && !osp(1, 200.5f, 0f) && !osp(1, float.NaN, 0f) && !osp(1, 1f, float.NaN) && !osp(1, 1f, 100001f),
            "Validação: ObjetoSpawn com tipo desconhecido, raio 0/negativo/acima de 200/NaN ou posição inválida recusado");
        Checar(V("ObjetoCabeNoAndar", 0, 0, true) && V("ObjetoCabeNoAndar", 5, 1, true) && V("ObjetoCabeNoAndar", 31, 2, false),
            "Validação: até 2 santuários amaldiçoados e 32 objetos por andar");
        Checar(!V("ObjetoCabeNoAndar", 5, 2, true) && !V("ObjetoCabeNoAndar", 32, 0, false), "Validação: 3º amaldiçoado ou 33º objeto no andar recusado");

        // Porta de entrada: sem Hello compatível, só Hello, Tchau e Expulso
        var tpr = T("HellClockCoop.Net.Protocolo");
        byte tipo(string n) => (byte)tpr.GetField(n).GetValue(null);
        Checar(V("PortaDeEntrada", tipo("TipoHello"), false, false) && V("PortaDeEntrada", tipo("TipoTchau"), false, false) && V("PortaDeEntrada", tipo("TipoExpulso"), true, false),
            "Validação: Hello, Tchau e Expulso passam sem Hello compatível");
        Checar(!V("PortaDeEntrada", tipo("TipoPlanoAndar"), false, false) && !V("PortaDeEntrada", tipo("TipoInimigoSpawn"), true, false) && !V("PortaDeEntrada", tipo("TipoGolpes"), false, true),
            "Validação: plano, inimigo e golpes de par sem Hello compatível descartados");
        Checar(V("PortaDeEntrada", tipo("TipoFimRun"), true, true), "Validação: mensagens de par com Hello compatível passam");

        // Golpes: compatível, mesmo andar e até 512 itens (sem teto de dano); desde a 0.21.0 a DLC diferente não recusa
        Checar(V("GolpesAceitos", 1, true, true) && V("GolpesAceitos", 512, true, true), "Validação: Golpes com 1 e 512 itens do mesmo andar aceito");
        Checar(!V("GolpesAceitos", 513, true, true) && !V("GolpesAceitos", 65535, true, true), "Validação: Golpes com 513 ou 65535 itens recusado inteiro");
        Checar(!V("GolpesAceitos", 1, false, true) && !V("GolpesAceitos", 1, true, false),
            "Validação: Golpes de par incompatível ou de outro andar recusado");
        Checar(V("DanoValido", 3.4e38f), "Validação: dano alto continua aceito (sem teto de dano)");

        // Relay obrigatório: ICE desligado (0) com quem não é amigo; padrão do usuário (-1) com amigos
        int ice(bool amigo) => (int)tv.GetMethod("IceDoPar").Invoke(null, new object[] { amigo });
        Checar(ice(false) == 0 && ice(true) == -1, "Validação: sem ICE (só relay) com quem não é amigo; amigos como a Steam escolher");
        Checar(V("CaminhoAceito", false, true) && V("CaminhoAceito", true, false) && V("CaminhoAceito", true, true) && !V("CaminhoAceito", false, false),
            "Validação: conexão direta só com amigo; quem não é amigo só pelo relay");

        // Reconexão no mesmo andar: volta a acompanhar só na mesma run e com o save liberado ali
        Checar(V("RetomaRunDoHost", 5u, 5u, true, true), "Validação: reconexão no mesmo andar, mesma run e save liberado volta a acompanhar");
        Checar(!V("RetomaRunDoHost", 6u, 5u, true, true) && !V("RetomaRunDoHost", 4u, 5u, true, true) && !V("RetomaRunDoHost", 5u, 5u, false, true) && !V("RetomaRunDoHost", 5u, 5u, true, false),
            "Validação: reconexão em outro andar, outra run ou sem o save liberado não volta a acompanhar");
    }

    // Status de controle do convidado (0.9.0): valores lidos de volta do codec passam pelo filtro e pelos tetos
    {
        var tSt = T("HellClockCoop.Net.StatusInimigos"); var tSti = T("HellClockCoop.Net.StatusInimigos+Item");
        (float dur, float inten, byte pilhas) lido(float d, float i, byte p)
        {
            var st = Activator.CreateInstance(tSt); var it = Activator.CreateInstance(tSti);
            tSti.GetField("Duracao").SetValue(it, d); tSti.GetField("Intensidade").SetValue(it, i); tSti.GetField("Pilhas").SetValue(it, p);
            ((System.Collections.IList)tSt.GetField("Itens").GetValue(st)).Add(it);
            var s2 = tSt.GetMethod("Desserializar").Invoke(null, new object[] { (byte[])tSt.GetMethod("Serializar").Invoke(st, null) });
            var i2 = ((System.Collections.IList)tSt.GetField("Itens").GetValue(s2))[0];
            return ((float)tSti.GetField("Duracao").GetValue(i2), (float)tSti.GetField("Intensidade").GetValue(i2), (byte)tSti.GetField("Pilhas").GetValue(i2));
        }
        bool SV((float d, float i, byte p) x) => V("StatusValido", x.d, x.i, x.p);
        float F(string m, float v) => (float)tv.GetMethod(m).Invoke(null, new object[] { v });
        Checar(SV(lido(2.5f, 1f, 1)) && SV(lido(0.01f, 0.5f, 255)) && SV(lido(900f, 50f, 3)), "Validação: status normal e acima do teto aceitos (o teto reduz, não recusa)");
        Checar(!SV(lido(0f, 1f, 1)) && !SV(lido(-1f, 1f, 1)) && !SV(lido(float.NaN, 1f, 1)) && !SV(lido(float.PositiveInfinity, 1f, 1)),
            "Validação: status com duração 0, negativa, NaN ou ∞ recusado");
        Checar(!SV(lido(2f, 0f, 1)) && !SV(lido(2f, -1f, 1)) && !SV(lido(2f, float.NaN, 1)) && !SV(lido(2f, float.NegativeInfinity, 1)) && !SV(lido(2f, 1f, 0)),
            "Validação: status com intensidade 0 (divisão no jogo), negativa, NaN, -∞ ou 0 pilhas recusado");
        Checar(F("DuracaoStatusLimitada", 900f) == 30f && F("DuracaoStatusLimitada", 30f) == 30f && F("DuracaoStatusLimitada", 2.5f) == 2.5f,
            "Validação: duração do status limitada a 30 s");
        Checar(F("IntensidadeStatusLimitada", 50f) == 10f && F("IntensidadeStatusLimitada", 0.1f) == 1f && F("IntensidadeStatusLimitada", 0.999f) == 1f
            && F("IntensidadeStatusLimitada", 1f) == 1f && F("IntensidadeStatusLimitada", 1.25f) == 1.25f,
            "Validação: intensidade do status limitada a 1–10 (abaixo de 1 a lentidão viraria reforço)");
        int P(byte p, int max, bool acumula = true) => (int)tv.GetMethod("PilhasStatusLimitadas").Invoke(null, new object[] { p, max, acumula });
        Checar(P(lido(2f, 1f, 255).pilhas, 5) == 5 && P(3, 5) == 3 && P(5, 5) == 5, "Validação: pilhas do status limitadas ao MaxStacks");
        Checar(P(255, 0) == 10 && P(255, -1) == 10 && P(4, 0) == 4, "Validação: status sem MaxStacks no jogo → até 10 pilhas");
        Checar(P(7, 5, false) == 1 && P(255, 0, false) == 1 && P(1, 0, false) == 1, "Validação: status que não acumula pilhas (CanStack falso) → 1 pilha no host");
        bool L(int stat, int tipo, float v) => V("ModificadorReduzVelocidade", stat, tipo, v);
        Checar(L(6, 0, -0.3f) && L(7, 0, -5f) && L(6, 1, 0.7f) && L(7, 2, 0.5f) && L(6, 1, 0f),
            "Validação: lentidão (movimento ou ataque; aditivo < 0, multiplicativo de 0 até < 1) aceita");
        Checar(!L(6, 0, 0f) && !L(6, 0, 0.2f) && !L(6, 1, 1f) && !L(6, 2, 1.3f) && !L(6, 1, -0.5f),
            "Validação: modificador de velocidade que não reduz (0, positivo, fator ≥ 1 ou negativo) recusado");
        Checar(!L(8, 0, -0.3f) && !L(131, 1, 0.5f) && !L(0, 0, -1f) && !L(6, 3, 0.5f) && !L(6, 4, 0f) && !L(6, 0, float.NaN) && !L(6, 1, float.NegativeInfinity),
            "Validação: outro atributo (dano, resistência, recarga, mínimo), override, NaN ou -∞ não é lentidão");
        Checar((int)tv.GetField("ItensStatusPorMensagemMaximo").GetValue(null) == 512 && (int)tv.GetField("AvisosStatusPorConvidado").GetValue(null) == 20,
            "Validação: tetos de 512 status por mensagem e 20 avisos por convidado");
    }
}


// Métricas: JSON local e corpo do Firestore
{
    var td = T("HellClockCoop.Balanceamento.Doc"); var ts = T("HellClockCoop.Balanceamento.Serializar");
    var add = td.GetMethod("Add");
    var andar = Activator.CreateInstance(td); add.Invoke(andar, new object[] { "indice", 3L }); add.Invoke(andar, new object[] { "segundos", 41.5 }); add.Invoke(andar, new object[] { "chefe", false });
    var d = Activator.CreateInstance(td); add.Invoke(d, new object[] { "formato", 1 }); add.Invoke(d, new object[] { "mod", "0.7.1 \"x\"" }); add.Invoke(d, new object[] { "duracao", 120.0 });
    add.Invoke(d, new object[] { "andares", new System.Collections.Generic.List<object> { andar } });
    string json = (string)ts.GetMethod("Json").Invoke(null, new[] { d });
    string fs = (string)ts.GetMethod("Firestore").Invoke(null, new[] { d });
    var j = System.Text.Json.JsonDocument.Parse(json).RootElement; var f = System.Text.Json.JsonDocument.Parse(fs).RootElement.GetProperty("fields");
    Checar(j.GetProperty("andares")[0].GetProperty("segundos").GetDouble() == 41.5 && j.GetProperty("mod").GetString() == "0.7.1 \"x\"", "Métricas: JSON local");
    Checar(f.GetProperty("formato").GetProperty("integerValue").GetString() == "1" && f.GetProperty("duracao").GetProperty("doubleValue").GetDouble() == 120.0
        && f.GetProperty("andares").GetProperty("arrayValue").GetProperty("values")[0].GetProperty("mapValue").GetProperty("fields").GetProperty("indice").GetProperty("integerValue").GetString() == "3", "Métricas: corpo do Firestore");

    // Resumos salvos antes do aceite: o JSON local lido de volta gera o mesmo corpo de envio (tipos preservados).
    var lido = ts.GetMethod("LerJson").Invoke(null, new object[] { json });
    Checar((string)ts.GetMethod("Firestore").Invoke(null, new[] { lido }) == fs, "Métricas: JSON local lido de volta = mesmo corpo do Firestore");
    var dInst = Activator.CreateInstance(td); add.Invoke(dInst, new object[] { "formato", 1 }); add.Invoke(dInst, new object[] { "instalacao", "0123456789abcdef0123456789abcdef" });
    add.Invoke(dInst, new object[] { "papel", "solo" }); add.Invoke(dInst, new object[] { "duracao", 0.5 });
    string jsonInst = (string)ts.GetMethod("Json").Invoke(null, new[] { dInst });
    var rpe = ts.GetMethod("ResumoParaEnvio");
    var argsRpe = new object[] { "run-20261121-093015.json", jsonInst, null, null };
    Checar((bool)rpe.Invoke(null, argsRpe) && (string)argsRpe[2] == "0123456789abcdef0123456789abcdef-20261121-093015"
        && (string)argsRpe[3] == (string)ts.GetMethod("Firestore").Invoke(null, new[] { dInst }), "Métricas: resumo salvo → id instalação-carimbo e corpo de envio");
    Checar(!(bool)rpe.Invoke(null, new object[] { "run-2026-11-21.json", jsonInst, null, null }), "Métricas: nome fora do padrão run-yyyyMMdd-HHmmss é ignorado");
    Checar(!(bool)rpe.Invoke(null, new object[] { "run-20261121-093015.json", "{\"instalacao\":", null, null }), "Métricas: resumo corrompido é ignorado");
    Checar(!(bool)rpe.Invoke(null, new object[] { "run-20261121-093015.json", json, null, null }), "Métricas: resumo sem id da instalação é ignorado");
    Checar(!(bool)rpe.Invoke(null, new object[] { "run-20261121-093015.json", jsonInst.Replace("0123456789abcdef0123456789abcdef", "../../x"), null, null }), "Métricas: id da instalação fora do formato (32 hex) é ignorado");
    Checar(!(bool)rpe.Invoke(null, new object[] { "run-20261121-093015.json", new string('[', 5000) + new string(']', 5000), null, null }), "Métricas: JSON aninhado demais é recusado sem estourar a pilha");
}

// Novidades (0.9.2): regras puras da janela e o texto gerado do docs/changelog.json
{
    var tN = T("HellClockCoop.Interface.Novidades");
    var abrir = tN.GetMethod("DeveAbrirSozinha", BindingFlags.Static | BindingFlags.NonPublic);
    var trocar = tN.GetMethod("TrocarAba", BindingFlags.Static | BindingFlags.NonPublic);
    var mostra = tN.GetMethod("MostraTestar", BindingFlags.Static | BindingFlags.NonPublic);
    bool Abre(string vista, string atual) => (bool)abrir.Invoke(null, new object[] { vista, atual });
    int Troca(int aba, int delta, int total) => (int)trocar.Invoke(null, new object[] { aba, delta, total });
    Checar(Abre("", "0.9.2") && Abre(null, "0.9.2"), "Novidades: abre sozinha na primeira instalação (nada visto)");
    Checar(Abre("0.9.1", "0.9.2"), "Novidades: abre sozinha depois de atualizar");
    Checar(!Abre("0.9.2", "0.9.2") && !Abre(" 0.9.2 ", "0.9.2"), "Novidades: não abre de novo na mesma versão (espaços do .cfg ignorados)");
    Checar(Troca(0, -1, 4) == 3 && Troca(3, 1, 4) == 0 && Troca(1, 1, 4) == 2 && Troca(2, 5, 0) == 0, "Novidades: troca de aba gira nas pontas e tolera lista vazia");

    var tV = T("HellClockCoop.Interface.VersaoNovidade");
    var versoes = (Array)T("HellClockCoop.Interface.NovidadesTexto").GetField("Versoes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    string Campo(object v, string f) => (string)tV.GetField(f, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(v);
    string[] Lista(object v, string f) => (string[])tV.GetField(f, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(v);
    // Plugin herda do BepInEx (fora do codectest): a constante da Novidades é cópia da PluginVersion, feita pelo compilador.
    string versaoMod = (string)tN.GetField("VersaoAtual", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    Checar(versoes.Length == 4, $"Novidades: 4 versões embutidas (atual + 3 anteriores): {versoes.Length}");
    Checar(versoes.Length > 0 && Campo(versoes.GetValue(0), "V") == versaoMod, $"Novidades: a primeira versão embutida é a do mod ({versaoMod}); rodar scripts/gerar-novidades.py");
    bool todasComItens = true;
    foreach (var v in versoes) todasComItens &= Lista(v, "Itens").Length > 0 && Lista(v, "Testar") != null && !string.IsNullOrEmpty(Campo(v, "Titulo"));
    Checar(todasComItens, "Novidades: toda versão embutida tem título e itens (testar nunca nulo)");
    // Regra, não dado: toda versão embutida não testada e com roteiro mostra "O que testar", e todo roteiro tem 3 a 5 itens.
    bool roteirosOk = true;
    int comRoteiro = 0;
    foreach (var v in versoes)
    {
        int n = Lista(v, "Testar").Length;
        if (n == 0) continue;
        comRoteiro++;
        roteirosOk &= n >= 3 && n <= 5;
        if (!(bool)tV.GetField("Testado", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(v))
            roteirosOk &= (bool)mostra.Invoke(null, new[] { v });
    }
    Checar(roteirosOk, $"Novidades: versões não testadas com roteiro mostram \"O que testar\"; roteiros com 3 a 5 itens ({comRoteiro} com roteiro)");
    var testada = Activator.CreateInstance(tV, BindingFlags.Instance | BindingFlags.NonPublic, null,
        new object[] { "0.0.1", "01/01/2026", "x", true, new[] { "a" }, new[] { "b" } }, null);
    var semRoteiro = Activator.CreateInstance(tV, BindingFlags.Instance | BindingFlags.NonPublic, null,
        new object[] { "0.0.2", "01/01/2026", "x", false, new[] { "a" }, null }, null);
    Checar(!(bool)mostra.Invoke(null, new[] { testada }) && !(bool)mostra.Invoke(null, new[] { semRoteiro }),
        "Novidades: versão testada ou sem roteiro não mostra \"O que testar\"");
}

// Menu do coop (0.9.3): foco da lista, confirmação de saída e liberação da entrada do jogo
{
    var tM = T("HellClockCoop.Interface.MenuCoop");
    var prox = tM.GetMethod("ProximoFoco", BindingFlags.Static | BindingFlags.NonPublic);
    var conf = tM.GetMethod("ConfirmaSaida", BindingFlags.Static | BindingFlags.NonPublic);
    var lib = T("HellClockCoop.Interface.BloqueioEntrada").GetMethod("DeveLiberar", BindingFlags.Static | BindingFlags.NonPublic);
    int P(int atual, int delta, params bool[] sel) => (int)prox.Invoke(null, new object[] { atual, delta, sel });
    bool C(float agora, float ate) => (bool)conf.Invoke(null, new object[] { agora, ate });
    bool L(int pedido, int quadro) => (bool)lib.Invoke(null, new object[] { pedido, quadro });
    // seção, item, info, item, item (desabilitado), item
    bool[] lista = { false, true, false, true, false, true };
    Checar(P(1, 1, lista) == 3 && P(3, 1, lista) == 5 && P(5, 1, lista) == 1, "Menu: descer pula cabeçalhos, linhas de info e itens indisponíveis, e gira no fim");
    Checar(P(1, -1, lista) == 5 && P(5, -1, lista) == 3, "Menu: subir gira do primeiro para o último");
    Checar(P(-1, 1, lista) == 1 && P(-1, -1, lista) == 5 && P(99, 1, lista) == 1, "Menu: sem foco válido começa pela ponta da direção");
    Checar(P(0, 1, false, false) == -1 && P(0, 1) == -1 && P(0, 1, null) == -1, "Menu: lista sem item selecionável não tem foco");
    Checar(P(1, 1, false, true, false) == 1, "Menu: um item só fica com o foco");
    Checar(!C(10f, -1f) && C(10f, 12f) && C(12f, 12f) && !C(12.1f, 12f), "Menu: sair do coop só no segundo aperto, dentro do prazo");
    Checar(!L(100, 100) && !L(100, 101) && L(100, 102) && L(-10, 0), "Entrada: libera o jogo só dois quadros depois do último pedido do painel");
    // 0.30.3: o Esc da tela de saves é segurado pelo patch no ouvinte, pelo mesmo tempo da trava completa
    var esc = T("HellClockCoop.Interface.BloqueioEntrada").GetMethod("DeveSegurarEsc", BindingFlags.Static | BindingFlags.NonPublic);
    bool E(bool leve, int pedido, int quadro) => (bool)esc.Invoke(null, new object[] { leve, pedido, quadro });
    Checar(E(false, 100, 100) && E(false, 100, 101) && !E(false, 100, 102) && !E(false, -10, 0), "Entrada: o Esc da tela de saves fica seguro até a trava do painel ser liberada");
    Checar(!E(true, 100, 100) && !E(true, 100, 101), "Entrada: a trava leve (TP até o aliado) não segura o Esc da tela de saves");

    // 0.12.0: abas e deslizantes do menu com a interface do jogo
    var aba = tM.GetMethod("ProximaAba", BindingFlags.Static | BindingFlags.NonPublic);
    var ajustar = tM.GetMethod("AjustarNumero", BindingFlags.Static | BindingFlags.NonPublic);
    var pelaFracao = tM.GetMethod("NumeroPelaFracao", BindingFlags.Static | BindingFlags.NonPublic);
    int A(int atual, int delta, int total) => (int)aba.Invoke(null, new object[] { atual, delta, total });
    float J(float v, int d, float passo, float min, float max) => (float)ajustar.Invoke(null, new object[] { v, d, passo, min, max });
    float F(float f, float passo, float min, float max) => (float)pelaFracao.Invoke(null, new object[] { f, passo, min, max });
    Checar(A(0, 1, 2) == 1 && A(1, 1, 2) == 0 && A(0, -1, 2) == 1 && A(5, 1, 2) == 0 && A(0, 1, 0) == 0, "Menu: LB/RB e setas trocam de aba girando nas pontas");
    Checar(J(0.8f, 1, 0.05f, 0f, 1f) == 0.85f && J(0.8f, -1, 0.05f, 0f, 1f) == 0.75f && J(1f, 1, 0.05f, 0f, 1f) == 1f && J(0f, -1, 0.05f, 0f, 1f) == 0f,
        "Menu: deslizante anda um passo e para nas pontas");
    Checar(J(0.83f, 1, 0.1f, 0.3f, 2f) == 0.9f && J(5f, -1, 0.1f, 0.3f, 2f) == 2f && J(float.NaN, 1, 0.1f, 0.3f, 2f) == 0.4f,
        "Menu: deslizante alinha ao passo e corrige valor fora da faixa ou inválido no .cfg");
    Checar(F(0f, 0.05f, 0f, 1f) == 0f && F(1f, 0.05f, 0f, 1f) == 1f && F(0.42f, 0.05f, 0f, 1f) == 0.4f && F(-3f, 0.1f, 0.3f, 2f) == 0.3f
           && F(2f, 0.1f, 0.3f, 2f) == 2f && F(float.NaN, 0.1f, 0.3f, 2f) == 0.3f && F(0.5f, 0.1f, 0f, 2f) == 1f,
        "Menu: clique na barra vira valor alinhado ao passo, dentro da faixa");

    // 0.13.0: aba Novidades na janela do coop (F10 e abertura automática) e espera antes de fechar
    var resp = tM.GetMethod("RespostaNovidades", BindingFlags.Static | BindingFlags.NonPublic);
    var liberado = tM.GetMethod("FechamentoLiberado", BindingFlags.Static | BindingFlags.NonPublic);
    string R(bool aberto, bool nativo, bool naAba, bool auto) => resp.Invoke(null, new object[] { aberto, nativo, naAba, auto }).ToString();
    bool Lib(float agora, float ate) => (bool)liberado.Invoke(null, new object[] { agora, ate });
    Checar(R(false, false, false, false) == "JanelaAntiga" && R(true, false, false, true) == "JanelaAntiga",
        "Novidades: sem a interface do jogo, usa a janela antiga (tecla e automática)");
    Checar(R(false, true, false, false) == "Abrir" && R(false, true, false, true) == "Abrir", "Novidades: menu fechado abre na aba Novidades");
    Checar(R(true, true, false, false) == "TrocarAba" && R(true, true, false, true) == "TrocarAba", "Novidades: menu aberto em outra aba troca para ela");
    Checar(R(true, true, true, false) == "Fechar" && R(true, true, true, true) == "Ficar", "Novidades: na aba, a F10 fecha e a abertura automática não");
    Checar(Lib(5f, 0f) && !Lib(5f, 6.5f) && Lib(6.5f, 6.5f), "Menu: Esc, B e [X] só fecham depois da espera da abertura automática");
    var nomesAbas = (string[])tM.GetField("NomesDasAbas", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    var tAba = T("HellClockCoop.Interface.MenuCoop+Aba");
    Checar(nomesAbas.Length == 4 && nomesAbas[1] == "Salas" && nomesAbas[3] == "Novidades" && Enum.GetNames(tAba).Length == 4
           && (int)Enum.Parse(tAba, "Salas") == 1 && (int)Enum.Parse(tAba, "Novidades") == 3,
        "Menu: quatro abas (Sessão, Salas, Ajustes, Novidades) com nome para cada uma, na ordem do enum");

    // 0.18.0: faixas de ação (Sala0 + i, Expulsar0 + i) sem colidir com as ações fixas
    var tAcao = T("HellClockCoop.Interface.MenuCoop+Acao");
    var faixa = tM.GetMethod("IndiceNaFaixa", BindingFlags.Static | BindingFlags.NonPublic);
    object Ac(int v) => Enum.ToObject(tAcao, v);
    int Fx(int acao, string inicio, int tam) => (int)faixa.Invoke(null, new object[] { Ac(acao), Enum.Parse(tAcao, inicio), tam });
    int sala0 = (int)Enum.Parse(tAcao, "Sala0"), exp0 = (int)Enum.Parse(tAcao, "Expulsar0");
    int maiorFixa = Enum.GetValues(tAcao).Cast<int>().Where(v => v < sala0).Max();
    Checar(maiorFixa < sala0 && sala0 + 50 <= exp0, "Menu: ações fixas abaixo de Sala0 e 50 salas antes de Expulsar0");
    Checar(Fx(sala0, "Sala0", 50) == 0 && Fx(sala0 + 49, "Sala0", 50) == 49 && Fx(sala0 + 50, "Sala0", 50) == -1 && Fx(sala0 - 1, "Sala0", 50) == -1,
        "Menu: Sala0 + i vira a sala i só dentro da faixa");
    Checar(Fx(exp0 + 2, "Expulsar0", 3) == 2 && Fx(exp0 + 3, "Expulsar0", 3) == -1 && Fx(sala0, "Expulsar0", 3) == -1,
        "Menu: Expulsar0 + i vira o aliado i (até 3)");
}

// 0.18.0: lista de salas (dados do lobby vêm de desconhecidos) e expulsão
{
    var ts = T("HellClockCoop.Net.Salas");
    object Sm(string m, params object[] a) => ts.GetMethod(m, BindingFlags.Static | BindingFlags.Public).Invoke(null, a);
    Checar((string)Sm("LimparNome", "Fu<b>la</b>no") == "Fubla/bno" && (string)Sm("LimparNome", "a\nb\tc") == "abc"
           && (string)Sm("LimparNome", (object)null) == "?" && (string)Sm("LimparNome", "   ") == "?" && (string)Sm("LimparNome", "<>") == "?",
        "Salas: nome sem < >, sem caracteres de controle e nunca vazio");
    Checar(((string)Sm("LimparNome", new string('x', 200))).Length == 32, "Salas: nome cortado em 32 caracteres");
    Checar((string)Sm("LimparNome", "ab" + (char)0x202E + "cd" + (char)0x200B + "e") == "abcde", "Salas: nome sem formatação invisível (U+202E, U+200B)");
    Checar((string)Sm("LimparNome", new string('x', 31) + "\U0001F600") == new string('x', 31), "Salas: corte em 32 não parte emoji (par substituto)");
    // 0.25.0: nome da sala
    Checar((string)Sm("LimparNome", "a\\nHost Fulano") == "anHost Fulano" && (string)Sm("LimparNome", "a" + (char)0x2028 + "b" + (char)0x2029 + "c") == "abc"
           && !(bool)Sm("CaractereAceito", '\\') && !(bool)Sm("CaractereAceito", (char)0x2028) && (bool)Sm("CaractereAceito", 'é'),
        "Salas: nome sem \\ (escape do TextMeshPro) nem U+2028/U+2029 (quebra de linha): não forja uma linha \"Host ...\"");
    Checar((string)Sm("NomePadrao", "Igor") == "Sala de Igor" && (string)Sm("NomePadrao", "<b>") == "Sala de b"
           && (string)Sm("NomePadrao", (object)null) == "Sala de ?" && ((string)Sm("NomePadrao", new string('x', 40))).Length == 32,
        "Sala: nome padrão \"Sala de <nome>\", limpo e no limite de 32");
    Checar((string)Sm("NomeDigitado", "  Os Malditos ") == "Os Malditos" && Sm("NomeDigitado", "   ") == null && Sm("NomeDigitado", "<>\n") == null
           && Sm("NomeDigitado", (object)null) == null && ((string)Sm("NomeDigitado", new string('y', 50))).Length == 32
           && (string)Sm("NomeDigitado", "a" + (char)0x202E + "b") == "ab",
        "Sala: nome digitado limpo; vazio ou só lixo vira null (padrão)");
    Checar((string)Sm("NomeDaSalaListada", "Os <i>Malditos", "Fulano") == "Os iMalditos" && (string)Sm("NomeDaSalaListada", "", "Fulano") == "Sala de Fulano"
           && (string)Sm("NomeDaSalaListada", (object)null, "Fulano") == "Sala de Fulano",
        "Sala: nome do lobby limpo; sem nome (ou lixo) cai em \"Sala de <host>\"");
    Checar((int)Sm("LerInteiro", "34", 999) == 34 && (int)Sm("LerInteiro", "1000", 999) == 0 && (int)Sm("LerInteiro", "-3", 999) == 0
           && (int)Sm("LerInteiro", "abc", 999) == 0 && (int)Sm("LerInteiro", "", 999) == 0 && (int)Sm("LerInteiro", (object)null, 999) == 0
           && (int)Sm("LerInteiro", "99999999999", 999) == 0,
        "Salas: número do lobby fora da faixa, negativo, vazio ou lixo vira 0");
    Checar((string)Sm("LerLugar", "run") == "run" && (string)Sm("LerLugar", "hub") == "hub" && (string)Sm("LerLugar", "menu") == "menu"
           && (string)Sm("LerLugar", "RUN") == "" && (string)Sm("LerLugar", (object)null) == "", "Salas: lugar só aceita menu, hub e run");
    Checar((int)Sm("AtoDoNome", "Act02_DungeonConfig") == 2 && (int)Sm("AtoDoNome", "act1") == 1 && (int)Sm("AtoDoNome", "Act12_X") == 12
           && (int)Sm("AtoDoNome", "Survival_Config") == 0 && (int)Sm("AtoDoNome", "Act_X") == 0 && (int)Sm("AtoDoNome", "Act99") == 0
           && (int)Sm("AtoDoNome", "") == 0 && (int)Sm("AtoDoNome", (object)null) == 0,
        "Salas: ato pelo nome do DungeonConfig (Act02 → 2); fora do padrão, 0");
    Checar((string)Sm("TextoLugar", "run", 2, 5) == "Ato 2, andar 5" && (string)Sm("TextoLugar", "run", 0, 3) == "em run, andar 3"
           && (string)Sm("TextoLugar", "run", 0, 0) == "em run" && (string)Sm("TextoLugar", "hub", 0, 0) == "no hub"
           && (string)Sm("TextoLugar", "menu", 0, 0) == "no menu" && (string)Sm("TextoLugar", "", 3, 3) == "lugar desconhecido",
        "Salas: texto do lugar (ato e andar, hub, menu, desconhecido)");
    var tSala = T("HellClockCoop.Net.SalaListada"); var sl = Activator.CreateInstance(tSala);
    foreach (var (c, v) in new (string, object)[] { ("Host", "Fulano"), ("Lugar", "run"), ("Ato", 2), ("Andar", 5), ("Nivel", 34), ("Jogadores", 2), ("Vagas", 4) })
        tSala.GetField(c).SetValue(sl, v);
    Checar((string)Sm("TextoSala", sl) == "Sala de Fulano" && (string)Sm("DetalheSala", sl) == "Host Fulano  ·  Ato 2, andar 5" && (string)Sm("ValorSala", sl) == "Nv 34  2/4",
        "Salas: sala em 2 linhas, nome da sala em cima (sem nome no lobby, \"Sala de <host>\") e host e lugar embaixo (0.25.0)");
    tSala.GetField("Nome").SetValue(sl, "Os Malditos");
    Checar((string)Sm("TextoSala", sl) == "Os Malditos", "Salas: nome da sala dado pelo host vai na linha 1");
    tSala.GetField("CursedWar").SetValue(sl, true);
    Checar((string)Sm("TextoSala", sl) == "Os Malditos  ·  Cursed War", "Salas: sala com a Cursed War ganha a marca na linha 1");
    tSala.GetField("CursedWar").SetValue(sl, false);
    tSala.GetField("Nivel").SetValue(sl, 0);
    Checar((string)Sm("ValorSala", sl) == "2/4", "Salas: sem nível (host no menu) mostra só as vagas");
    bool Vis(string mod, string proto, int j, int v, bool nossa, bool exp) =>
        (bool)Sm("SalaVisivel", mod, proto, "0.18.0", "19", j, v, nossa, exp);
    Checar(Vis("0.18.0", "19", 1, 4, false, false) && Vis("0.18.0", "19", 3, 4, false, false),
        "Salas: mesma versão e protocolo, com vaga, aparece (com ou sem a DLC desde a 0.21.0)");
    Checar(!Vis("0.17.0", "19", 1, 4, false, false) && !Vis("0.18.0", "18", 1, 4, false, false) && !Vis(null, "19", 1, 4, false, false),
        "Salas: outra versão do mod ou outro protocolo (ou sem dado) não aparece");
    Checar(!Vis("0.18.0", "19", 4, 4, false, false) && !Vis("0.18.0", "19", 0, 4, false, false)
           && !Vis("0.18.0", "19", 1, 0, false, false) && !Vis("0.18.0", "19", 1, 4, true, false)
           && !Vis("0.18.0", "19", 1, 4, false, true),
        "Salas: cheia, vazia, sem vagas, a nossa ou de onde fomos expulsos não aparece");

    // 0.21.0: DLC Cursed War — só o Ato 4 trava quem não tem a DLC
    Checar((bool)Sm("PrecisaDaDlc", true, false) && !(bool)Sm("PrecisaDaDlc", true, true) && !(bool)Sm("PrecisaDaDlc", false, false)
           && !(bool)Sm("PrecisaDaDlc", false, true), "DLC: só run do Ato 4 sem a Cursed War trava; jogo base nunca trava");
    var sd = Activator.CreateInstance(tSala);
    foreach (var (c, v) in new (string, object)[] { ("Host", "Beltrano"), ("Lugar", "run"), ("Ato", 4), ("Andar", 2), ("Nivel", 61), ("Jogadores", 1),
                 ("Vagas", 4), ("Dungeon", 88), ("Recorde", 0L), ("CursedWar", true), ("SemDlc", true) })
        tSala.GetField(c).SetValue(sd, v);
    Checar((bool)Sm("SalaIndisponivel", sd) && (string)Sm("ValorSala", sd) == "indisponível"
           && (string)Sm("DetalheSala", sd) == "Host Beltrano  ·  Ato 4, andar 2  ·  precisa da DLC Cursed War",
        "DLC: sala do Ato 4 sem a DLC fica indisponível e o motivo da DLC vem antes do recorde");
    tSala.GetField("SemDlc").SetValue(sd, false); tSala.GetField("Recorde").SetValue(sd, 9L);
    Checar(!(bool)Sm("SalaIndisponivel", sd) && (string)Sm("ValorSala", sd) == "Nv 61  1/4" && (string)Sm("DetalheSala", sd) == "Host Beltrano  ·  Ato 4, andar 2",
        "DLC: com a DLC e o save no andar, a sala do Ato 4 fica disponível");
    Checar((string)Sm("TextoForaDaRunDlc", 4, 2) == "O host foi para uma run da DLC Cursed War (Ato 4, andar 2), que você não tem. Você fica no hub e acompanha a próxima run do jogo base.",
        "DLC: aviso do convidado sem a DLC quando o host vai ao Ato 4");
    Checar((string)Sm("TextoDlcDiferente", "Ana", true) == "Ana tem a DLC Cursed War e você não tem: vocês jogam juntos os atos 1 a 3; o Ato 4 é só para quem tem a DLC."
           && ((string)Sm("TextoDlcDiferente", "Ana", false)).StartsWith("Ana não tem a DLC Cursed War e você tem"),
        "DLC: aviso de DLC diferente no aperto de mão");
    Checar((string)Sm("TextoParFora", "Ana", true) == "Ana não tem a DLC Cursed War: fica no hub, fora da escala do grupo."
           && (string)Sm("TextoParFora", "Ana", false) == "Ana ainda não chegou aqui na campanha: fica no hub, fora da escala do grupo.",
        "DLC: o host vê o motivo certo de quem não acompanha (DLC ou save)");
    Checar((bool)Sm("ExpulsaoValida", 5ul, 5ul, 9ul) && !(bool)Sm("ExpulsaoValida", 6ul, 5ul, 9ul) && !(bool)Sm("ExpulsaoValida", 9ul, 9ul, 9ul)
           && !(bool)Sm("ExpulsaoValida", 0ul, 0ul, 9ul), "Salas: Expulso só vale do dono do lobby, nunca de nós mesmos nem de id 0");
    Checar((bool)Sm("PodeBuscar", 10f, -1f) && !(bool)Sm("PodeBuscar", 10f, 8f) && (bool)Sm("PodeBuscar", 11f, 8f),
        "Salas: nova busca só 3 s depois da anterior");

    // 0.23.0: bloquear jogador (bloqueados.txt e o dono anunciado no lobby).
    var tb = T("HellClockCoop.Net.Bloqueados");
    object Sb(string m, params object[] a) => tb.GetMethod(m, BindingFlags.Static | BindingFlags.Public).Invoke(null, a);
    const ulong idA = 76561197960287930ul, idB = 76561198000000001ul;
    Checar((bool)Sb("IdValido", idA) && !(bool)Sb("IdValido", 0ul) && !(bool)Sb("IdValido", 12345ul)
           && !(bool)Sb("IdValido", 103582791429521408ul /* grupo: tipo 7 */) && !(bool)Sb("IdValido", 76561197960265728ul /* conta 0 */),
        "Bloqueio: só SteamID64 de conta individual conta como jogador");
    var lidos = (List<KeyValuePair<ulong, string>>)Sb("Ler",
        "# comentário\r\n" + idA + "\tAna\r\n\r\nlixo\tX\n" + idA + "\tAna de novo\n" + idB + "\n12345\tCurto\n" + idB + "0\tGrande\n");
    Checar(lidos.Count == 2 && lidos[0].Key == idA && lidos[0].Value == "Ana" && lidos[1].Key == idB && lidos[1].Value == "?",
        "Bloqueio: Ler ignora comentário, linha ruim, repetida e id inválido; sem nome vira ?");
    var ida = new List<KeyValuePair<ulong, string>> { new KeyValuePair<ulong, string>(idA, "An\ta\n<b>"), new KeyValuePair<ulong, string>(idB, "Bia") };
    var volta = (List<KeyValuePair<ulong, string>>)Sb("Ler", (string)Sb("Formatar", ida));
    Checar(volta.Count == 2 && volta[0].Key == idA && volta[0].Value == "Anab" && volta[1].Value == "Bia",
        "Bloqueio: Formatar e Ler vão e voltam, com o nome limpo (sem TAB, quebra nem rich text)");
    var muitos = new System.Text.StringBuilder();
    for (int k = 1; k <= 600; k++) muitos.Append(76561197960265728ul + (ulong)k).Append('\n');
    Checar(((List<KeyValuePair<ulong, string>>)Sb("Ler", muitos.ToString())).Count == 500, "Bloqueio: Ler para no teto de 500");
    Checar((ulong)Sm("LerSteamId", idA.ToString()) == idA && (ulong)Sm("LerSteamId", "") == 0ul && (ulong)Sm("LerSteamId", "abc") == 0ul
           && (ulong)Sm("LerSteamId", "12345") == 0ul && (ulong)Sm("LerSteamId", "-1") == 0ul && (ulong)Sm("LerSteamId", idA + "000") == 0ul,
        "Salas: dono do lobby (hcc_dono) só vale se for SteamID de jogador");
    var tp = T("HellClockCoop.Net.Protocolo");
    Checar((int)tp.GetField("Versao").GetValue(null) == 25 && (byte)tp.GetField("TipoExpulso").GetValue(null) == 13
           && (byte)tp.GetField("TipoExpulsoAviso").GetValue(null) == 14 && (byte)tp.GetField("TipoForaDaRun").GetValue(null) == 29
           && (byte)tp.GetField("TipoPainelAliado").GetValue(null) == 15 && (byte)tp.GetField("TipoPedidoTp").GetValue(null) == 16
           && (byte)tp.GetField("TipoRespostaTp").GetValue(null) == 17,
        "Protocolo 25 (InimigosEstado só com o que mudou, 0.31.0) com Expulso tipo=13 (1 byte), ExpulsoAviso tipo=14 (0.21.1), ForaDaRun tipo=29 (0.19.0), PainelAliado tipo=15 (0.27.0) e PedidoTp/RespostaTp tipo=16/17");

    // 0.19.0: trava pelo progresso (maxFloorReached conta a partir de 1, como o andar do lobby)
    Checar((int)Sm("LerHash", "-123456789") == -123456789 && (int)Sm("LerHash", "2147483647") == 2147483647
           && (int)Sm("LerHash", "abc") == 0 && (int)Sm("LerHash", "") == 0 && (int)Sm("LerHash", (object)null) == 0
           && (int)Sm("LerHash", "99999999999") == 0 && (int)Sm("LerHash", "123456789012") == 0,
        "Salas: hash da masmorra aceita qualquer int (até negativo); lixo, vazio ou fora do int vira 0");
    Checar((bool)Sm("Liberado", 5, 5L) && (bool)Sm("Liberado", 5, 9L) && !(bool)Sm("Liberado", 5, 4L) && !(bool)Sm("Liberado", 1, 0L)
           && !(bool)Sm("Liberado", 1, -1L) && (bool)Sm("Liberado", 0, -1L),
        "Trava: recorde ≥ andar do host libera; andar 1 de masmorra nunca começada e leitura falha travam; fora da run não trava");
    var st = Activator.CreateInstance(tSala);
    void Ps(string lugar, int dungeon, int andar, long recorde)
    {
        tSala.GetField("Lugar").SetValue(st, lugar); tSala.GetField("Dungeon").SetValue(st, dungeon);
        tSala.GetField("Andar").SetValue(st, andar); tSala.GetField("Recorde").SetValue(st, recorde);
        tSala.GetField("Jogadores").SetValue(st, 1); tSala.GetField("Vagas").SetValue(st, 4); tSala.GetField("Host").SetValue(st, "Fulano");
    }
    bool Trav() => (bool)Sm("SalaTravada", st);
    Ps("run", 77, 5, 2); bool t1 = Trav(); string v1 = (string)Sm("ValorSala", st);
    Ps("run", 77, 5, 0); string v0 = (string)Sm("ValorSala", st);
    Ps("run", 77, 5, 5); bool t2 = Trav();
    Ps("run", 77, 5, -1); bool t3 = Trav();
    Ps("run", 0, 5, 2); bool t4 = Trav();
    Ps("hub", 77, 0, 0); bool t5 = Trav(); string v5 = (string)Sm("ValorSala", st);
    Checar(t1 && !t2 && !t3 && !t4 && !t5, "Trava: sala na run além do recorde trava; recorde alcançado, sem save, atalho especial (sem masmorra) e hub não");
    Ps("run", 77, 5, 2); string d1 = (string)Sm("DetalheSala", st);
    Ps("run", 77, 5, 0); string d0 = (string)Sm("DetalheSala", st);
    Checar(v1 == "indisponível" && v0 == "indisponível" && v5 == "1/4" && d1 == "Host Fulano  ·  em run, andar 5  ·  você chegou ao andar 2"
           && d0 == "Host Fulano  ·  em run, andar 5  ·  você não começou essa masmorra",
        "Trava: sala travada fica indisponível à direita e mostra o recorde na linha 2 (0.21.0)");
    Checar((string)Sm("TextoForaDaRun", 3, 5, 2L) == "O seu save não chegou aonde o host está (Ato 3, andar 5; você chegou ao andar 2). Você fica no hub e acompanha a próxima run liberada.",
        "Trava: aviso do convidado que não acompanha");
}

// Balanceamento: valores fixos do autor e XP/ouro/Soulstones/moedas em coop (cada um × fator, arredondamento sorteado)
{
    var te = T("HellClockCoop.Balanceamento.Escalonamento");
    // XP e ouro: valores do autor fixos no código desde a 0.16.3 (fora do .cfg)
    var ff = te.GetMethod("FatorFixo", BindingFlags.NonPublic | BindingFlags.Static);
    foreach (var nome in new[] { "XpGrupo", "OuroGrupo" })
    {
        var lista = (float[])te.GetField(nome, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        float G(int n) => (float)ff.Invoke(null, new object[] { lista, n });
        Checar(Math.Abs(G(2) - 0.83f) < 1e-6f && Math.Abs(G(3) - 0.71f) < 1e-6f && Math.Abs(G(4) - 0.63f) < 1e-6f && G(1) == 1f && Math.Abs(G(5) - 0.63f) < 1e-6f,
            $"{nome}: fixo 0,83 / 0,71 / 0,63 para 2 / 3 / 4 jogadores, solo ×1, além da lista o último");
    }
    var tc = T("HellClockCoop.Fase2.JogadorCaido");
    float Const(string n) => (float)tc.GetField(n, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    Checar(Const("VidaAoVoltar") == 0.5f && Const("SegundosPorQueda") == 10f, "Caído: volta com 50% e −10 s por queda, fixos no código (0.16.3)");
    var es = te.GetMethod("Escalar", BindingFlags.NonPublic | BindingFlags.Static);
    double E(double v, float f, double sorteio) => (double)es.Invoke(null, new object[] { v, f, sorteio });

    // 0.22.0: vida, chefes, dano, quantidade e tempo também fixos no código (sem .cfg nem liga/desliga)
    foreach (var (nome, v2, v3, v4) in new[] { ("VidaGrupo", 1.7f, 2.4f, 3.1f), ("VidaChefeGrupo", 1.8f, 2.6f, 3.4f), ("DanoGrupo", 1.1f, 1.2f, 1.3f),
                                               ("QuantidadeGrupo", 1.2f, 1.4f, 1.6f), ("TempoGrupo", 0.55f, 0.4f, 0.32f) })
    {
        var lista = (float[])te.GetField(nome, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        float G(int n) => (float)ff.Invoke(null, new object[] { lista, n });
        Checar(G(1) == 1f && G(2) == v2 && G(3) == v3 && G(4) == v4 && G(5) == v4, $"{nome}: fixo {v2} / {v3} / {v4} para 2 / 3 / 4 jogadores, solo ×1");
    }
    Checar(te.GetMethod("FatorDaLista", BindingFlags.NonPublic | BindingFlags.Static) == null
           && te.GetField("_ativo", BindingFlags.NonPublic | BindingFlags.Static) == null,
        "Balanceamento: sem leitura do .cfg e sem liga/desliga (0.22.0)");
    var antigas = (string[])te.GetField("ChavesAntigas", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    Checar(antigas.SequenceEqual(new[] { "Ativo", "VidaInimigos", "VidaChefes", "DanoInimigos", "QuantidadeInimigos", "TempoGanho" }),
        "Balanceamento: as 6 opções antigas do .cfg são apagadas do arquivo");

    Checar(E(100, 1f, 0.99) == 100 && E(7.5, 1f, 0) == 7.5, "Escalar: fator 1 não mexe no valor");
    Checar(E(0, 0.83f, 0) == 0, "Escalar: zero continua zero");
    Checar(E(10, 0.83f, 0.29) == 9 && E(10, 0.83f, 0.31) == 8, "Escalar: 10 × 0,83 = 8,3 → 9 se o sorteio < 0,3, senão 8");
    Checar(E(1, 0.63f, 0.62) == 1 && E(1, 0.63f, 0.64) == 0, "Escalar: 1 × 0,63 → 1 com 63% de chance (moeda/XP pequeno não some sempre)");
    Checar(E(250, 0.71f, 0.999) == 177 && E(250, 0.71f, 0) == 178, "Escalar: 250 × 0,71 = 177,5 → 177 ou 178");
    Checar(E(-5, 0.83f, 0) == -5 && double.IsNaN(E(double.NaN, 0.83f, 0)), "Escalar: valor negativo ou NaN passa sem mexer");
    // a média sobre sorteios uniformes tem de ser valor × fator (sem viés para cima nem para baixo)
    foreach (var (v, f) in new[] { (1.0, 0.63f), (3.0, 0.83f), (37.0, 0.71f), (1234.0, 0.63f) })
    {
        double soma = 0; const int passos = 10000;
        for (int i = 0; i < passos; i++) soma += E(v, f, (i + 0.5) / passos);
        Checar(Math.Abs(soma / passos - v * f) < 1e-3, $"Escalar: média de {v} × {f} = {soma / passos:0.####} (esperado {v * f:0.####})");
    }
}

// 0.21.1: segurança das salas, fatia 3a-2
{
    var tv = T("HellClockCoop.Net.Validacao");
    bool V(string m, params object[] a) => (bool)tv.GetMethod(m).Invoke(null, a);

    var av = IdaVolta("ExpulsoAviso", new (string, object)[] { ("SteamId", 76561198000000001ul) }, 14);
    Checar(((byte[])T("HellClockCoop.Net.ExpulsoAviso").GetMethod("Serializar").Invoke(av, null)).Length == 9, "ExpulsoAviso: 1 byte de tipo + 8 do SteamID");
    try { T("HellClockCoop.Net.ExpulsoAviso").GetMethod("Desserializar").Invoke(null, new object[] { new byte[] { 13 } }); Checar(false, "Expulso lido como ExpulsoAviso deveria falhar"); }
    catch (TargetInvocationException e) { Checar(e.InnerException is FormatException || e.InnerException is System.IO.EndOfStreamException, "ExpulsoAviso: Expulso (tipo 13) não é lido como aviso"); }
    Checar(V("AvisoExpulsaoValido", 5ul, 5ul, 7ul, 9ul), "ExpulsoAviso: do host sobre um terceiro vale");
    Checar(!V("AvisoExpulsaoValido", 6ul, 5ul, 7ul, 9ul) && !V("AvisoExpulsaoValido", 0ul, 0ul, 7ul, 9ul),
        "ExpulsoAviso: de quem não é o host (ou id 0) é recusado");
    Checar(!V("AvisoExpulsaoValido", 5ul, 5ul, 9ul, 9ul) && !V("AvisoExpulsaoValido", 5ul, 5ul, 5ul, 9ul) && !V("AvisoExpulsaoValido", 5ul, 5ul, 0ul, 9ul),
        "ExpulsoAviso: sobre nós mesmos, sobre o próprio host ou id 0 é recusado");
    Checar(!(bool)T("HellClockCoop.Net.Salas").GetMethod("ExpulsaoValida").Invoke(null, new object[] { 6ul, 5ul, 9ul }),
        "Host fixo: com a run aberta do host 5, Expulso do novo dono do lobby (6) não vale");

    Checar(V("AndarEspecialValido", 0) && V("AndarEspecialValido", 1) && V("AndarEspecialValido", 2), "Atalho: nenhum, DesertShortcut (1) e LaboratoryShortcut (2) passam");
    Checar(!V("AndarEspecialValido", 3) && !V("AndarEspecialValido", -1) && !V("AndarEspecialValido", 99) && !V("AndarEspecialValido", int.MinValue),
        "Atalho: Sandbox (3) e valores fora do enum são recusados (não pulam as travas de progresso e DLC)");
    Checar(V("SpawnValido", 0, 3) && V("SpawnValido", 2, 3) && !V("SpawnValido", 3, 3) && !V("SpawnValido", -1, 3) && !V("SpawnValido", int.MinValue, 3) && !V("SpawnValido", 0, 0),
        "SpawnAndar: índice só de 0 a total-1");

    int maxI = (int)tv.GetField("InimigosPorAndarMaximo").GetValue(null), maxC = (int)tv.GetField("ChefesPorAndarMaximo").GetValue(null);
    Checar(V("InimigoCabeNoAndar", 0, 120, 0, (byte)0) && V("InimigoCabeNoAndar", 119, 120, maxC, (byte)0) && !V("InimigoCabeNoAndar", 120, 120, 0, (byte)0),
        "Inimigos: até o teto da contagem (comuns não contam no teto de chefes)");
    Checar(V("InimigoCabeNoAndar", 10, 120, maxC - 1, (byte)3) && !V("InimigoCabeNoAndar", 10, 120, maxC, (byte)3) && !V("InimigoCabeNoAndar", 10, 120, maxC, (byte)6)
           && V("InimigoCabeNoAndar", 10, 120, maxC, (byte)2), $"Inimigos: até {maxC} chefes (Boss 3 e Unique 6) por andar; campeão (2) não conta");
    int TI(int doJogo, float q, int folga) => (int)tv.GetMethod("TetoInimigos").Invoke(null, new object[] { doJogo, q, folga });
    Checar(TI(100, 1.6f, 40) == 280 && TI(100, 0.5f, 0) == 150 && TI(0, 1.6f, 40) == 40,
        "Teto do andar 0.22.0: dados do jogo × fator de quantidade (mínimo 1) × folga 1,5 + folga fixa");
    Checar(TI(-1, 1.6f, 40) == maxI && TI(100, float.NaN, 40) == maxI && TI(5000, 1.6f, 40) == maxI && TI(int.MaxValue, 1.6f, 40) == maxI,
        $"Teto do andar: sem dados (-1) ou fator inválido, o genérico {maxI}; nunca acima dele");
    int TS(int ondas, float q) => (int)tv.GetMethod("TetoSobrevivencia").Invoke(null, new object[] { ondas, q });
    int evento = (int)tv.GetField("SobrevivenciaEventoMaximo").GetValue(null);
    int semOndas = (int)tv.GetField("SemOndas").GetValue(null);
    Checar(TS(semOndas, 1.6f) == evento && TS(10, 1.6f) == evento && TS(1000, 1.6f) == TI(1000, 1.6f, (int)tv.GetField("FolgaInimigosAndar").GetValue(null)),
        $"Teto de sobrevivência: o de evento ({evento}) sem ondas no andar; com ondas, o maior entre elas e o de evento");
    Checar(TS(-1, 1.6f) == maxI, $"Teto de sobrevivência: dados não lidos (-1), o genérico {maxI}");
    byte CS(byte aj, bool sobrev, bool tc = true) => (byte)tv.GetMethod("ContagemDoSpawn").Invoke(null, new object[] { aj, tc, sobrev });
    Checar(CS(0, false) == 0 && CS(0, true) == 1 && CS(1, false) == 2 && CS(1, true) == 2 && CS(1 | 4, false) == 0 && CS(1 | 8, true) == 1,
        "Contagem do spawn: invocação à parte só sem XP e sem tesouro próprios; sobrevivência pela marca");
    Checar(CS(2, false) == 0 && CS(2, true) == 1 && CS(3, false) == 2,
        "Contagem do spawn: descartável (mantém o loot) fica na contagem do andar ou da sobrevivência");
    Checar(CS(1 | 8, false, false) == 2 && CS(1 | 8, true, false) == 2 && CS(1 | 4 | 8, false, false) == 0,
        "Contagem do spawn: invocação com o tesouro vazio do SetupSummon (EmptyTreasureClass) continua invocação; com XP, não");
    bool RP(byte aj, float xp, bool tc, bool sobrev, byte tipo) => (bool)tv.GetMethod("RecompensaPermitida").Invoke(null, new object[] { aj, xp, tc, sobrev, tipo });
    Checar(RP(4, 10f, false, true, 0) && RP(8, 1f, true, false, 3) && RP(8, 1f, true, false, 6) && RP(4 | 8, 10f, true, true, 0),
        "Recompensa: XP acima de 1 e tesouro próprio valem em sobrevivência e chefes (Boss 3, Unique 6)");
    Checar(!RP(4, 10f, false, false, 0) && !RP(8, 1f, true, false, 2) && RP(4, 1f, false, false, 0) && RP(8, 1f, false, false, 0) && RP(1 | 2, 1f, false, false, 0),
        "Recompensa: inimigo comum não recebe XP acima de 1 nem tesouro próprio");
    Checar(RP(1 | 8, 1f, false, false, 0), "Recompensa: invocação comum com o tesouro vazio do SetupSummon (EmptyTreasureClass) passa");
    int maxB = (int)tv.GetField("BausPorAndarMaximo").GetValue(null);
    int TB(int doJogo) => (int)tv.GetMethod("TetoBaus").Invoke(null, new object[] { doJogo });
    Checar(TB(-1) == maxB && TB(6) == 10 && TB(100) == maxB && V("BauCabeNoAndar", 9, 10) && !V("BauCabeNoAndar", 10, 10),
        $"Baús 0.22.0: dados do andar + folga 4, até {maxB}; sem dados, {maxB}");
    Checar(V("AndarLimpoNovo", 0u, -1L, 0, 0) && V("AndarLimpoNovo", 4u, 3L, 0, 9) && !V("AndarLimpoNovo", 4u, 4L, 9, 9),
        "AndarLimpo: no andar novo dispara; repetido no mesmo andar sem inimigos novos, não");
    Checar(V("AndarLimpoNovo", 4u, 4L, 12, 9), "AndarLimpo: no mesmo andar, de novo depois de inimigos novos (ondas, chefe)");
    ulong HE(ulong run, ulong dono) => (ulong)tv.GetMethod("HostEfetivo").Invoke(null, new object[] { run, dono });
    Checar(HE(0, 5) == 5 && HE(5, 5) == 5, "Host fixo: sem run, o dono do lobby; com run, o host dela enquanto é o dono");
    Checar(HE(5, 6) == 0 && !(bool)tv.GetMethod("AvisoExpulsaoValido").Invoke(null, new object[] { 5ul, HE(5, 6), 6ul, 9ul }),
        "Host fixo: lobby trocou de dono no meio da run → ninguém manda (nem o host que saiu e voltou expulsa o novo dono)");

    Checar(V("OrdemDeObjetoAceita", false, true, true, false) && !V("OrdemDeObjetoAceita", false, true, false, true),
        "Baú/quebrável/santuário: o convidado só obedece ao host");
    Checar(V("OrdemDeObjetoAceita", true, false, false, true) && !V("OrdemDeObjetoAceita", true, false, false, false),
        "Baú/quebrável/santuário: o host aceita de quem está no mesmo andar");
    Checar(!V("OrdemDeObjetoAceita", false, false, true, true), "Baú/quebrável/santuário: fora do coop da run, nada");
    Checar(V("BonusDoParAceito", true, true, false) && !V("BonusDoParAceito", false, true, false) && !V("BonusDoParAceito", true, false, false) && !V("BonusDoParAceito", true, true, true),
        "TimerBonus: só com a run aberta, do par compatível e dentro da escala");

    var tSn = T("HellClockCoop.Net.PlayerSnapshot");
    object Snap(Action<object> ajuste)
    {
        var o = Activator.CreateInstance(tSn);
        tSn.GetField("Rotacao").SetValue(o, new UnityEngine.Quaternion(0, 0, 0, 1)); tSn.GetField("RotacaoModelo").SetValue(o, new UnityEngine.Quaternion(0, 0, 0, 1));
        tSn.GetField("Posicao").SetValue(o, new UnityEngine.Vector3(10, 0, -5));
        ajuste(o);
        return tSn.GetMethod("Desserializar").Invoke(null, new object[] { (byte[])tSn.GetMethod("Serializar").Invoke(o, null) });
    }
    bool SV(object o) => (bool)tv.GetMethod("SnapshotValido").Invoke(null, new[] { o });
    Checar(SV(Snap(o => { })), "Snapshot: valores normais passam");
    Checar(!SV(Snap(o => tSn.GetField("Posicao").SetValue(o, new UnityEngine.Vector3(float.NaN, 0, 0)))) && !SV(Snap(o => tSn.GetField("Posicao").SetValue(o, new UnityEngine.Vector3(0, 2e5f, 0)))),
        "Snapshot: posição NaN ou além do teto recusada");
    Checar(!SV(Snap(o => tSn.GetField("Tempo").SetValue(o, float.PositiveInfinity))) && !SV(Snap(o => tSn.GetField("Rotacao").SetValue(o, new UnityEngine.Quaternion(float.NaN, 0, 0, 1)))),
        "Snapshot: tempo infinito ou rotação NaN recusados");
    var tFr = T("HellClockCoop.Net.AnimatorFrame");
    Checar(!SV(Snap(o => { ((bool[])tSn.GetField("AnimatorAtivo").GetValue(o))[1] = true; var fr = ((Array)tSn.GetField("Animators").GetValue(o)).GetValue(1);
                         tFr.GetField("StateHashes").SetValue(fr, new[] { 1 }); tFr.GetField("NormalizedTimes").SetValue(fr, new[] { 0.5f }); tFr.GetField("LayerWeights").SetValue(fr, new[] { 1f });
                         tFr.GetField("Floats").SetValue(fr, new[] { float.NaN }); })),
        "Snapshot: float de animator NaN recusado");
    Checar(!SV(Snap(o => { tSn.GetField("MascaraOssos").SetValue(o, (byte)1); ((UnityEngine.Quaternion[])tSn.GetField("RotacoesOssos").GetValue(o))[0] = new UnityEngine.Quaternion(0, float.NegativeInfinity, 0, 1); })),
        "Snapshot: rotação de osso infinita recusada");
}

// PainelAliado (0.27.0): skills equipadas e até 4 status do painel de grupo
{
    var t = T("HellClockCoop.Net.PainelAliado");
    var ts = T("HellClockCoop.Net.PainelAliado+Status");
    object Ler(byte[] b) => t.GetMethod("Desserializar").Invoke(null, new object[] { b });
    byte[] Ser(object o) => (byte[])t.GetMethod("Serializar").Invoke(o, null);
    System.Collections.IList Skills(object o) => (System.Collections.IList)t.GetField("Skills").GetValue(o);
    System.Collections.IList Efeitos(object o) => (System.Collections.IList)t.GetField("Efeitos").GetValue(o);
    object St(int h, byte a) { var s = Activator.CreateInstance(ts); ts.GetField("Hash").SetValue(s, h); ts.GetField("Acumulos").SetValue(s, a); return s; }
    bool Recusa(byte[] bytes) { try { Ler(bytes); return false; } catch (TargetInvocationException e) when (e.InnerException is FormatException) { return true; } }

    var o = Activator.CreateInstance(t);
    Skills(o).Add(123456789); Skills(o).Add(0); Skills(o).Add(-42);
    Efeitos(o).Add(St(-987654321, 1)); Efeitos(o).Add(St(55, 255));
    var b = Ser(o);
    var pa2 = Ler(b);
    Checar(b[0] == 15 && Skills(pa2).Count == 3 && (int)Skills(pa2)[0] == 123456789 && (int)Skills(pa2)[1] == 0 && (int)Skills(pa2)[2] == -42
        && Efeitos(pa2).Count == 2 && (int)ts.GetField("Hash").GetValue(Efeitos(pa2)[0]) == -987654321 && (byte)ts.GetField("Acumulos").GetValue(Efeitos(pa2)[1]) == 255,
        $"PainelAliado ida e volta ({b.Length} bytes)");
    Checar((bool)t.GetMethod("IgualA").Invoke(o, new[] { pa2 }), "PainelAliado: IgualA depois da ida e volta");
    Efeitos(pa2).RemoveAt(1);
    Checar(!(bool)t.GetMethod("IgualA").Invoke(o, new[] { pa2 }) && !(bool)t.GetMethod("IgualA").Invoke(o, new object[] { null }), "PainelAliado: IgualA vê status a menos e nulo");
    var pVazio = Ler(Ser(Activator.CreateInstance(t)));
    Checar(Skills(pVazio).Count == 0 && Efeitos(pVazio).Count == 0, "PainelAliado vazio (sem personagem) ida e volta");
    var pMuitos = Activator.CreateInstance(t);
    for (int k = 0; k < 12; k++) { Skills(pMuitos).Add(k + 1); Efeitos(pMuitos).Add(St(k + 1, 1)); }
    var pMv = Ler(Ser(pMuitos));
    Checar(Skills(pMv).Count == 8 && Efeitos(pMv).Count == 4, "PainelAliado: Serializar corta em 8 skills e 4 status");
    Checar(Recusa(new byte[] { 15, 9 }), "PainelAliado: 9 skills no cabeçalho derrubam a mensagem");
    Checar(Recusa(new byte[] { 15, 0, 5 }), "PainelAliado: 5 status no cabeçalho derrubam a mensagem");
    bool cortada; try { Ler(new byte[] { 15, 1, 1, 2 }); cortada = false; } catch (TargetInvocationException e) when (e.InnerException is System.IO.EndOfStreamException) { cortada = true; }
    Checar(cortada, "PainelAliado: mensagem cortada no meio recusada (fim do fluxo, como os outros codecs; o transporte descarta)");
}

// Recepção com teto por quadro (0.31.1): só a recepção muda, nenhuma mensagem mudou de formato (protocolo 25).
{
    var tv = T("HellClockCoop.Net.Validacao");
    object C(string campo) => tv.GetField(campo).GetValue(null);
    int D(float vence, float agora, int aplicados, int teto, bool instantaneo) => (int)tv.GetMethod("DecidirNaFila").Invoke(null, new object[] { vence, agora, aplicados, teto, instantaneo });
    int aplicar = (int)C("FilaAplicar"), esperar = (int)C("FilaEsperar"), descartar = (int)C("FilaDescartar");
    float atraso = (float)C("AtrasoCosmeticoMaximo");
    int capacidade = (int)C("FilaCosmeticaMaxima");

    var cosmeticos = new List<int>();
    for (int k = 0; k < 256; k++) if ((bool)tv.GetMethod("CosmeticoDeAliado").Invoke(null, new object[] { (byte)k })) cosmeticos.Add(k);
    Checar(cosmeticos.SequenceEqual(new[] { 50, 51, 52, 53, 54, 55, 56, 57 }),
        $"Recepção simulada: só as 8 mensagens de imagem e som voltam a este PC ({string.Join(",", cosmeticos)}); golpes (34), mortes (31), plano (20), bônus de tempo (23) e marcador (28) ficam de fora");

    Checar(D(10f, 10f, 0, 3, true) == aplicar && D(10f, 10f, 2, 3, false) == aplicar, "Teto por quadro: com vaga no quadro o item vencido é aplicado");
    Checar(D(10f, 10f, 3, 3, true) == esperar && D(10f, 10.2f, 3, 3, false) == esperar && D(10f, 10f, 7, 3, true) == esperar, "Teto por quadro: sem vaga o item espera o quadro seguinte");
    Checar(D(10f, 10f + atraso + 0.01f, 0, 3, true) == descartar && D(10f, 10f + atraso + 0.01f, 3, 3, true) == descartar,
        $"Teto por quadro: o instantâneo com mais de {atraso} s de atraso é descartado, com ou sem vaga");
    Checar(D(10f, 10f + atraso - 0.01f, 0, 3, true) == aplicar, "Teto por quadro: o instantâneo um pouco abaixo do atraso máximo ainda é aplicado");
    Checar(D(10f, 500f, 0, 3, false) == aplicar && D(10f, 500f, 3, 3, false) == esperar, "Teto por quadro: o que dura nunca é descartado por atraso, só espera");
    Checar((int)C("NumerosDanoPorQuadroMaximo") > 0 && (int)C("GolpesVistosPorQuadroMaximo") > 0 && (int)C("SonsPorQuadroMaximo") > 0 && (int)C("CopiasFxPorQuadroMaximo") > 0,
        $"Teto por quadro: números {C("NumerosDanoPorQuadroMaximo")}, golpes vistos {C("GolpesVistosPorQuadroMaximo")}, sons {C("SonsPorQuadroMaximo")}, cópias de efeito {C("CopiasFxPorQuadroMaximo")}");

    int U(int aplicados, double ms, int teto, double orcamento) => (int)tv.GetMethod("VagasUsadas").Invoke(null, new object[] { aplicados, ms, teto, orcamento });
    Checar(U(0, 0.0, 3, 4.0) == 0 && U(2, 3.9, 3, 4.0) == 2, "Orçamento de tempo das cópias: abaixo do orçamento valem as cópias feitas (a primeira do quadro sempre tem vaga)");
    Checar(U(1, 14.0, 3, 4.0) == 3 && D(10f, 10f, U(1, 14.0, 3, 4.0), 3, false) == esperar && U(5, 4.0, 3, 4.0) == 5,
        "Orçamento de tempo das cópias: uma cópia pesada (14 ms) fecha o quadro, e as seguintes esperam");
    Checar((double)C("CopiasFxPorQuadroMs") > 0.0, $"Orçamento de tempo das cópias: {C("CopiasFxPorQuadroMs")} ms por quadro");

    // A fila de verdade (FilaComTeto<int>), com teto de 3 por quadro.
    var tFila = T("HellClockCoop.Net.FilaComTeto`1").MakeGenericType(typeof(int));
    object tipoNumero = Enum.ToObject(T("HellClockCoop.Desempenho+Cosmetico"), 0);
    object Nova(int teto) => Activator.CreateInstance(tFila, tipoNumero, teto);
    void Por(object f, float vence, int item, bool instantaneo) => tFila.GetMethod("Por").Invoke(f, new object[] { vence, item, instantaneo });
    void PorSemCusto(object f, float vence, int item) => tFila.GetMethod("PorSemCusto").Invoke(f, new object[] { vence, item });
    int Quantos(object f) => (int)tFila.GetProperty("Quantidade").GetValue(f);
    List<int> Processar(object f, float agora) { var saiu = new List<int>(); tFila.GetMethod("Processar").Invoke(f, new object[] { agora, (Action<int>)saiu.Add }); return saiu; }

    var fila = Nova(3);
    for (int k = 0; k < 10; k++) Por(fila, 1f, k, true);
    Checar(Processar(fila, 0.9f).Count == 0 && Quantos(fila) == 10, "Fila com teto: nada sai antes de vencer");
    Checar(Processar(fila, 1f).SequenceEqual(new[] { 0, 1, 2 }) && Quantos(fila) == 7, "Fila com teto: 10 itens vencidos, 3 saem no quadro, na ordem de chegada");
    Checar(Processar(fila, 1.1f).SequenceEqual(new[] { 3, 4, 5 }) && Processar(fila, 1.2f).SequenceEqual(new[] { 6, 7, 8 }), "Fila com teto: os seguintes saem nos quadros seguintes");
    Checar(Processar(fila, 1f + atraso + 0.05f).Count == 0 && Quantos(fila) == 0, "Fila com teto: o instantâneo que sobrou além do atraso máximo é descartado, não aplicado");

    var travada = Nova(3);
    for (int k = 0; k < 6; k++) Por(travada, 1f + k * 0.1f, k, true); // vencem em 1,0 ... 1,5; o quadro seguinte só vem em 1,65 (travada)
    Checar(Processar(travada, 1.65f).SequenceEqual(new[] { 4, 5 }) && Quantos(travada) == 0, "Fila com teto: depois de uma travada, o acumulado velho é descartado e só o recente sai");

    var ordem = Nova(1);
    Por(ordem, 1f, 100, false); Por(ordem, 1f, 200, false); PorSemCusto(ordem, 1f, -200); PorSemCusto(ordem, 1f, -100); Por(ordem, 1f, 300, false);
    Checar(Processar(ordem, 1f).SequenceEqual(new[] { 100 }), "Fila com teto: o fim (sem custo) não passa na frente do som que ainda espera a vez");
    Checar(Processar(ordem, 1.05f).SequenceEqual(new[] { 200, -200, -100 }), "Fila com teto: os fins saem junto com o item que os antecede, sem contar no teto");
    Checar(Processar(ordem, 50f).SequenceEqual(new[] { 300 }) && Quantos(ordem) == 0, "Fila com teto: o que dura sai mesmo muito atrasado");

    var fins = Nova(1);
    PorSemCusto(fins, 1f, 1); PorSemCusto(fins, 1f, 2); PorSemCusto(fins, 1f, 3);
    Checar(Processar(fins, 90f).SequenceEqual(new[] { 1, 2, 3 }), "Fila com teto: itens sem custo saem todos, sem teto e sem descarte por atraso");

    var cheia = Nova(3);
    for (int k = 0; k < capacidade + 50; k++) Por(cheia, 1f, k, true);
    Checar(Quantos(cheia) == capacidade, $"Fila com teto: acima de {capacidade} à espera, o que custa é descartado ao chegar");
    PorSemCusto(cheia, 1f, -1);
    Checar(Quantos(cheia) == capacidade + 1, "Fila com teto: o fim entra mesmo com a fila no limite");
    tFila.GetMethod("Limpar").Invoke(cheia, null);
    Checar(Quantos(cheia) == 0, "Fila com teto: Limpar esvazia (fim da sessão)");
}

Console.WriteLine(falhas == 0 ? "\nTODOS OS TESTES PASSARAM" : $"\n{falhas} FALHA(S)");
return falhas;
