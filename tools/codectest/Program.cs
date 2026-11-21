using System; using System.Reflection; using System.Linq;
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
Checar(bs[0] == 1 && bs[1] == 3, $"Snapshot tipo=1 versão=3 ({bs.Length} bytes)");
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
    ("IndiceLocal", 2u), ("TemSementeBoss", true), ("SementeBoss", 4000000000u), ("BossForcado", -1), ("DefinicaoHash", 8), ("EhBoss", true) }, 20);
IdaVolta("SpawnAndar", new (string, object)[] { ("IndiceAndar", 3u), ("IndiceSpawn", 2) }, 21);
IdaVolta("TimerSync", new (string, object)[] { ("Timer", 417.25f), ("TimerRelaxado", 999.5f) }, 22);
IdaVolta("TimerBonus", new (string, object)[] { ("Segundos", 15f) }, 23);
IdaVolta("FimRun", new (string, object)[] { ("Motivo", 3) }, 24);
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
IdaVolta("InimigoMorte", new (string, object)[] { ("Id", (ushort)65000), ("SemDrop", true) }, 31);
IdaVolta("InimigoSumiu", new (string, object)[] { ("Id", (ushort)9) }, 32);
IdaVolta("AndarLimpo", new (string, object)[] { ("Andar", 12u) }, 35);
{
    // Estado: 2 itens, um com animação
    var tE = T("HellClockCoop.Net.InimigosEstado"); var tI = T("HellClockCoop.Net.InimigosEstado+Item");
    var e = Activator.CreateInstance(tE); tE.GetField("Sequencia").SetValue(e, 77u);
    var lista = (System.Collections.IList)tE.GetField("Itens").GetValue(e);
    var tFr = T("HellClockCoop.Net.AnimatorFrame");
    for (int k = 0; k < 2; k++)
    {
        var i = Activator.CreateInstance(tI);
        tI.GetField("Id").SetValue(i, (ushort)(100 + k));
        tI.GetField("Posicao").SetValue(i, new UnityEngine.Vector3(k, 2f, -3.5f));
        tI.GetField("Yaw").SetValue(i, 90f);
        tI.GetField("Vida").SetValue(i, 0.5f);
        tI.GetField("JaNasceu").SetValue(i, k == 1);
        tI.GetField("Agressivo").SetValue(i, k == 1);
        if (k == 1)
        {
            var fr = Activator.CreateInstance(tFr);
            tFr.GetField("StateHashes").SetValue(fr, new[] { 42 }); tFr.GetField("NormalizedTimes").SetValue(fr, new[] { 0.3f });
            tFr.GetField("LayerWeights").SetValue(fr, new[] { 1f }); tFr.GetField("Floats").SetValue(fr, new[] { 0.9f });
            tI.GetField("Animacao").SetValue(i, fr);
        }
        lista.Add(i);
    }
    var b = (byte[])tE.GetMethod("Serializar").Invoke(e, null);
    var e2 = tE.GetMethod("Desserializar").Invoke(null, new object[] { b });
    var l2 = (System.Collections.IList)tE.GetField("Itens").GetValue(e2);
    var ia = l2[0]; var ic = l2[1];
    Checar(b[0] == 33 && (uint)tE.GetField("Sequencia").GetValue(e2) == 77u && l2.Count == 2, $"InimigosEstado tipo=33, 2 itens ({b.Length} bytes)");
    Checar((ushort)tI.GetField("Id").GetValue(ic) == 101 && ((UnityEngine.Vector3)tI.GetField("Posicao").GetValue(ic)).Equals(new UnityEngine.Vector3(1, 2f, -3.5f)), "InimigosEstado.Item Id/Posição");
    Checar(Math.Abs((float)tI.GetField("Yaw").GetValue(ic) - 90f) < 0.01f && Math.Abs((float)tI.GetField("Vida").GetValue(ic) - 0.5f) < 0.0001f, "InimigosEstado.Item Yaw/Vida (16 bits)");
    Checar(!(bool)tI.GetField("JaNasceu").GetValue(ia) && (bool)tI.GetField("JaNasceu").GetValue(ic), "InimigosEstado.Item JaNasceu");
    Checar(!(bool)tI.GetField("Agressivo").GetValue(ia) && (bool)tI.GetField("Agressivo").GetValue(ic), "InimigosEstado.Item Agressivo");
    Checar(tI.GetField("Animacao").GetValue(ia) == null && ((int[])tFr.GetField("StateHashes").GetValue(tI.GetField("Animacao").GetValue(ic))).SequenceEqual(new[] { 42 }), "InimigosEstado.Item Animação opcional");
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

    // ObjetoUsado: Concluido (purificação) só do host; Usado de qualquer par
    byte est(byte e) => (byte)Campo(Lido("ObjetoUsado", new (string, object)[] { ("Id", (ushort)12), ("Estado", e) }), "Estado");
    Checar(V("ObjetoUsadoAceito", est(2), true) && !V("ObjetoUsadoAceito", est(2), false), "Validação: ObjetoUsado Concluido só do host");
    Checar(V("ObjetoUsadoAceito", est(1), true) && V("ObjetoUsadoAceito", est(1), false), "Validação: ObjetoUsado Usado de qualquer par");

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

Console.WriteLine(falhas == 0 ? "\nTODOS OS TESTES PASSARAM" : $"\n{falhas} FALHA(S)");
return falhas;
