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
