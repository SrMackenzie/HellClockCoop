using System;
using System.Collections.Generic;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Fila de recepção com teto por quadro (0.31.1) para o que chega dos aliados e é só imagem e som. Cada item tem a
    /// hora em que vence (o atraso que acompanha o avatar); a cada quadro saem, na ordem de chegada, os vencidos até o
    /// teto. A decisão de cada um é do <see cref="Validacao.DecidirNaFila"/>. Item sem custo (o fim de um som) sai junto,
    /// sem contar no teto e sem nunca ser descartado, mas respeita a ordem: não passa na frente do som que ele encerra.
    /// </summary>
    internal sealed class FilaComTeto<T>
    {
        private struct Entrada
        {
            public float Vence;
            public bool Custa, Instantaneo;
            public T Item;
        }

        private readonly Queue<Entrada> _fila = new Queue<Entrada>();
        private readonly Desempenho.Cosmetico _tipo;
        private readonly int _tetoPorQuadro;

        public FilaComTeto(Desempenho.Cosmetico tipo, int tetoPorQuadro)
        {
            _tipo = tipo;
            _tetoPorQuadro = tetoPorQuadro;
        }

        public int Quantidade => _fila.Count;

        public void Limpar() => _fila.Clear();

        /// <summary>Item que custa. <paramref name="instantaneo"/>: atrasado demais, é descartado em vez de aplicado.</summary>
        public void Por(float vence, T item, bool instantaneo)
        {
            if (_fila.Count >= Validacao.FilaCosmeticaMaxima)
            {
                Desempenho.Descartou(_tipo, 1);
                return;
            }
            _fila.Enqueue(new Entrada { Vence = vence, Custa = true, Instantaneo = instantaneo, Item = item });
        }

        /// <summary>Item sem custo, que precisa chegar (um fim). Só uma enxurrada muito acima do teto da fila o derruba.</summary>
        public void PorSemCusto(float vence, T item)
        {
            if (_fila.Count >= Validacao.FilaCosmeticaMaxima * 4) return;
            _fila.Enqueue(new Entrada { Vence = vence, Item = item });
        }

        /// <summary>Aplica os vencidos até o teto do quadro. O <paramref name="aplicar"/> trata os próprios erros.</summary>
        public void Processar(float agora, Action<T> aplicar)
        {
            int aplicados = 0;
            while (_fila.Count > 0)
            {
                Entrada e = _fila.Peek();
                if (e.Vence > agora) return;
                if (e.Custa)
                {
                    int decisao = Validacao.DecidirNaFila(e.Vence, agora, aplicados, _tetoPorQuadro, e.Instantaneo);
                    if (decisao == Validacao.FilaEsperar)
                    {
                        Desempenho.NoTeto(_tipo);
                        return;
                    }
                    _fila.Dequeue();
                    if (decisao == Validacao.FilaDescartar)
                    {
                        Desempenho.Descartou(_tipo, 1);
                        continue;
                    }
                    aplicados++;
                }
                else _fila.Dequeue();
                aplicar(e.Item);
            }
        }
    }
}
