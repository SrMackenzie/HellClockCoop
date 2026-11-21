using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HellClockCoop.Balanceamento
{
    /// <summary>Campos em ordem de inserção (o resumo da run).</summary>
    internal sealed class Doc : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> _campos = new List<KeyValuePair<string, object>>();
        public void Add(string nome, object valor) => _campos.Add(new KeyValuePair<string, object>(nome, valor));
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _campos.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _campos.GetEnumerator();
    }

    /// <summary>JSON simples (arquivo local) e o formato tipado da API REST do Firestore (envio).</summary>
    internal static class Serializar
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static string Json(Doc doc)
        {
            var sb = new StringBuilder();
            Valor(sb, doc);
            return sb.ToString();
        }

        private static void Valor(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: Texto(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(C)); break;
                case long l: sb.Append(l.ToString(C)); break;
                case double d: sb.Append(Numero(d)); break;
                case float f: sb.Append(Numero(f)); break;
                case Doc doc:
                    sb.Append('{');
                    bool primeiro = true;
                    foreach (var kv in doc)
                    {
                        if (!primeiro) sb.Append(',');
                        primeiro = false;
                        Texto(sb, kv.Key);
                        sb.Append(':');
                        Valor(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case IList lista:
                    sb.Append('[');
                    for (int i = 0; i < lista.Count; i++) { if (i > 0) sb.Append(','); Valor(sb, lista[i]); }
                    sb.Append(']');
                    break;
                default: Texto(sb, Convert.ToString(v, C)); break;
            }
        }

        /// <summary>Corpo de criação de documento da API REST: {"fields": {nome: {tipoValue: ...}}}.</summary>
        public static string Firestore(Doc doc)
        {
            var sb = new StringBuilder();
            sb.Append("{\"fields\":");
            Campos(sb, doc);
            sb.Append('}');
            return sb.ToString();
        }

        private static void Campos(StringBuilder sb, Doc doc)
        {
            sb.Append('{');
            bool primeiro = true;
            foreach (var kv in doc)
            {
                if (!primeiro) sb.Append(',');
                primeiro = false;
                Texto(sb, kv.Key);
                sb.Append(':');
                Tipado(sb, kv.Value);
            }
            sb.Append('}');
        }

        private static void Tipado(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("{\"nullValue\":null}"); break;
                case string s: sb.Append("{\"stringValue\":"); Texto(sb, s); sb.Append('}'); break;
                case bool b: sb.Append("{\"booleanValue\":").Append(b ? "true" : "false").Append('}'); break;
                case int i: sb.Append("{\"integerValue\":\"").Append(i.ToString(C)).Append("\"}"); break;
                case long l: sb.Append("{\"integerValue\":\"").Append(l.ToString(C)).Append("\"}"); break;
                case double d: sb.Append("{\"doubleValue\":").Append(Numero(d)).Append('}'); break;
                case float f: sb.Append("{\"doubleValue\":").Append(Numero(f)).Append('}'); break;
                case Doc doc: sb.Append("{\"mapValue\":{\"fields\":"); Campos(sb, doc); sb.Append("}}"); break;
                case IList lista:
                    sb.Append("{\"arrayValue\":{\"values\":[");
                    for (int i = 0; i < lista.Count; i++) { if (i > 0) sb.Append(','); Tipado(sb, lista[i]); }
                    sb.Append("]}}");
                    break;
                default: sb.Append("{\"stringValue\":"); Texto(sb, Convert.ToString(v, C)); sb.Append('}'); break;
            }
        }

        /// <summary>
        /// Resumo salvo antes do aceite (metricas/run-yyyyMMdd-HHmmss.json) → id do documento e corpo do envio.
        /// O id é o mesmo que a run teria se o aceite já estivesse ligado (instalação + carimbo do nome do arquivo).
        /// </summary>
        public static bool ResumoParaEnvio(string nomeArquivo, string json, out string id, out string corpo)
        {
            id = null;
            corpo = null;
            if (!NomeDeResumo(nomeArquivo)) return false;
            string carimbo = nomeArquivo.Substring(4, 15);
            Doc doc;
            try { doc = LerJson(json) as Doc; }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
            if (doc == null) return false;
            string instalacao = null;
            foreach (var kv in doc) if (kv.Key == "instalacao") instalacao = kv.Value as string;
            if (!Hex32(instalacao)) return false; // o id vira nome de arquivo e precisa passar nas regras do banco
            id = instalacao + "-" + carimbo;
            corpo = Firestore(doc);
            return true;
        }

        /// <summary>Nome de resumo salvo: run-yyyyMMdd-HHmmss.json.</summary>
        public static bool NomeDeResumo(string nomeArquivo)
        {
            if (nomeArquivo == null || nomeArquivo.Length != 24 || !nomeArquivo.StartsWith("run-", StringComparison.Ordinal)
                || !nomeArquivo.EndsWith(".json", StringComparison.Ordinal)) return false;
            for (int i = 4; i < 19; i++)
            {
                char c = nomeArquivo[i];
                if (i == 12 ? c != '-' : c < '0' || c > '9') return false;
            }
            return true;
        }

        private static bool Hex32(string s)
        {
            if (s == null || s.Length != 32) return false;
            foreach (char c in s)
            {
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            }
            return true;
        }

        private const int ProfundidadeMaxima = 16;

        /// <summary>Lê o JSON que Json() grava. Número com ponto ou expoente volta como double; sem, como long.</summary>
        public static object LerJson(string texto)
        {
            if (texto == null) throw new FormatException("JSON vazio");
            int i = 0;
            object v = LerValor(texto, ref i, 0);
            Espacos(texto, ref i);
            if (i != texto.Length) throw new FormatException("texto sobrando depois do JSON");
            return v;
        }

        private static void Espacos(string t, ref int i)
        {
            while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\r' || t[i] == '\n' || t[i] == '﻿')) i++;
        }

        private static void Esperar(string t, ref int i, char c)
        {
            Espacos(t, ref i);
            if (i >= t.Length || t[i] != c) throw new FormatException("esperava '" + c + "' na posição " + i);
            i++;
        }

        private static bool Palavra(string t, ref int i, string p)
        {
            if (string.CompareOrdinal(t, i, p, 0, p.Length) != 0) return false;
            i += p.Length;
            return true;
        }

        private static object LerValor(string t, ref int i, int profundidade)
        {
            if (profundidade > ProfundidadeMaxima) throw new FormatException("JSON aninhado demais");
            Espacos(t, ref i);
            if (i >= t.Length) throw new FormatException("JSON incompleto");
            char c = t[i];
            if (c == '{')
            {
                var doc = new Doc();
                i++;
                Espacos(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return doc; }
                while (true)
                {
                    Espacos(t, ref i);
                    string nome = LerTexto(t, ref i);
                    Esperar(t, ref i, ':');
                    doc.Add(nome, LerValor(t, ref i, profundidade + 1));
                    Espacos(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    Esperar(t, ref i, '}');
                    return doc;
                }
            }
            if (c == '[')
            {
                var lista = new List<object>();
                i++;
                Espacos(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return lista; }
                while (true)
                {
                    lista.Add(LerValor(t, ref i, profundidade + 1));
                    Espacos(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    Esperar(t, ref i, ']');
                    return lista;
                }
            }
            if (c == '"') return LerTexto(t, ref i);
            if (Palavra(t, ref i, "true")) return true;
            if (Palavra(t, ref i, "false")) return false;
            if (Palavra(t, ref i, "null")) return null;
            int inicio = i;
            bool fracao = false;
            while (i < t.Length && (t[i] >= '0' && t[i] <= '9' || t[i] == '-' || t[i] == '+' || t[i] == '.' || t[i] == 'e' || t[i] == 'E'))
            {
                if (t[i] == '.' || t[i] == 'e' || t[i] == 'E') fracao = true;
                i++;
            }
            if (i == inicio) throw new FormatException("valor inesperado na posição " + i);
            string n = t.Substring(inicio, i - inicio);
            if (fracao) return double.Parse(n, NumberStyles.Float, C);
            return long.Parse(n, NumberStyles.AllowLeadingSign, C);
        }

        private static string LerTexto(string t, ref int i)
        {
            if (i >= t.Length || t[i] != '"') throw new FormatException("esperava texto na posição " + i);
            i++;
            var sb = new StringBuilder();
            while (i < t.Length)
            {
                char ch = t[i++];
                if (ch == '"') return sb.ToString();
                if (ch != '\\') { sb.Append(ch); continue; }
                if (i >= t.Length) break;
                char e = t[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > t.Length) throw new FormatException("escape \\u incompleto");
                        sb.Append((char)int.Parse(t.Substring(i, 4), NumberStyles.HexNumber, C));
                        i += 4;
                        break;
                    default: sb.Append(e); break; // \" \\ \/
                }
            }
            throw new FormatException("texto sem fim");
        }

        private static string Numero(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            string s = d.ToString("0.###", C);
            return s.IndexOf('.') < 0 ? s + ".0" : s; // sempre com ponto: o Firestore guarda como double
        }

        private static void Texto(StringBuilder sb, string valor)
        {
            sb.Append('"');
            foreach (char ch in valor ?? "")
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append(' ');
                else sb.Append(ch);
            }
            sb.Append('"');
        }
    }
}
