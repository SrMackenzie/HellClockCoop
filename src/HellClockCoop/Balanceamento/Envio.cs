using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HellClockCoop.Fase2;
using Steamworks;
using UnityEngine;

namespace HellClockCoop.Balanceamento
{
    /// <summary>
    /// Envio OPCIONAL do resumo das runs, só com aceite ([Metricas] EnviarResumoAnonimo = true no .cfg).
    ///
    /// - Só sai o mesmo resumo numérico salvo em metricas/ (sem nome da Steam, SteamID, caminhos ou log).
    /// - Destino: Firestore de um projeto exclusivo deste mod. As regras do banco só permitem CRIAR documentos
    ///   no formato esperado; ninguém lê, altera ou apaga pela internet (a leitura é pelo console do Firebase).
    /// - Transporte: ISteamHTTP (o cliente da Steam faz o HTTPS), sem depender do TLS do Mono do jogo.
    /// - Só runs salvas com o aceite ligado entram na fila (metricas/pendentes). Sem internet, tenta de novo depois.
    /// </summary>
    internal static class Envio
    {
        private static ConfigEntry<bool> _aceite;
        private static bool _avisou;
        private static float _proximaTentativa;
        private static bool _enviando;
        private static string _arquivoAtual;
        private static CallResult<HTTPRequestCompleted_t> _resposta;
        private static readonly Queue<string> Fila = new Queue<string>();

        private static string Pasta => Path.Combine(Paths.BepInExRootPath, "HellClockCoop", "metricas");
        private static string Pendentes => Path.Combine(Pasta, "pendentes");
        private static string Recusadas => Path.Combine(Pasta, "recusadas");

        internal static void Configurar(ConfigFile config)
        {
            _aceite = config.Bind("Metricas", "EnviarResumoAnonimo", false,
                "Envia o resumo de cada run (dados do jogo: tempo por andar, inimigos mortos, dano, mortes, dificuldade, " +
                "dungeon, resultado, versões, data e hora) para calibrar o balanceamento do coop. Sem nome da Steam, SteamID " +
                "ou arquivos do computador; vai junto um id aleatório desta instalação (BepInEx/HellClockCoop/instalacao.txt), " +
                "que não identifica você e serve para pedir a exclusão dos seus resumos por issue no GitHub do projeto. " +
                "Nada é enviado enquanto estiver false. Os resumos sempre ficam também em BepInEx/HellClockCoop/metricas.");
        }

        private static bool Ligado => _aceite != null && _aceite.Value && EnvioConfig.Configurado;

        /// <summary>Chamado quando um resumo é salvo no fim da run.</summary>
        internal static void RunSalva(string id, Doc doc)
        {
            if (!Ligado)
            {
                if (!_avisou && _aceite != null && !_aceite.Value)
                {
                    _avisou = true;
                    RunCompartilhada.Sessao?.Avisar("Resumo da run salvo. Para ajudar no balanceamento, ative EnviarResumoAnonimo no .cfg do mod (só dados do jogo, sem nome nem SteamID).");
                }
                return;
            }
            try
            {
                Directory.CreateDirectory(Pendentes);
                File.WriteAllText(Path.Combine(Pendentes, id + ".json"), Serializar.Firestore(doc), new UTF8Encoding(false));
                _proximaTentativa = 0f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Métricas] Não deu para pôr o resumo na fila de envio: {(e.InnerException ?? e).Message}");
            }
        }

        public static void Atualizar()
        {
            if (!Ligado || _enviando || Time.unscaledTime < _proximaTentativa) return;
            _proximaTentativa = Time.unscaledTime + 30f;
            try
            {
                if (Fila.Count == 0)
                {
                    if (!Directory.Exists(Pendentes)) { _proximaTentativa = Time.unscaledTime + 300f; return; }
                    foreach (string f in Directory.GetFiles(Pendentes, "*.json")) Fila.Enqueue(f);
                    if (Fila.Count == 0) { _proximaTentativa = Time.unscaledTime + 300f; return; }
                }
                Enviar(Fila.Dequeue());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Métricas] Envio: {(e.InnerException ?? e).Message}");
                _enviando = false;
            }
        }

        private static void Enviar(string arquivo)
        {
            if (!File.Exists(arquivo)) return;
            string id = Path.GetFileNameWithoutExtension(arquivo);
            byte[] corpo = File.ReadAllBytes(arquivo);
            string url = "https://firestore.googleapis.com/v1/projects/" + EnvioConfig.Projeto +
                         "/databases/(default)/documents/runs?documentId=" + Uri.EscapeDataString(id) +
                         "&key=" + Uri.EscapeDataString(EnvioConfig.Chave);

            HTTPRequestHandle req = SteamHTTP.CreateHTTPRequest(EHTTPMethod.k_EHTTPMethodPOST, url);
            if (req == HTTPRequestHandle.Invalid) { Plugin.Log.LogWarning("[Métricas] A Steam não abriu a requisição de envio."); return; }
            SteamHTTP.SetHTTPRequestNetworkActivityTimeout(req, 20);
            SteamHTTP.SetHTTPRequestRawPostBody(req, "application/json", corpo, (uint)corpo.Length);
            if (!SteamHTTP.SendHTTPRequest(req, out SteamAPICall_t chamada))
            {
                SteamHTTP.ReleaseHTTPRequest(req);
                Plugin.Log.LogWarning("[Métricas] A Steam recusou o envio agora; tento de novo depois.");
                return;
            }
            if (_resposta == null) _resposta = CallResult<HTTPRequestCompleted_t>.Create(Concluido);
            _enviando = true;
            _arquivoAtual = arquivo;
            _resposta.Set(chamada);
        }

        private static void Concluido(HTTPRequestCompleted_t r, bool falhaIo)
        {
            string arquivo = _arquivoAtual;
            _enviando = false;
            _arquivoAtual = null;
            try
            {
                int codigo = (int)r.m_eStatusCode;
                string nome = Path.GetFileName(arquivo ?? "");
                if (falhaIo || !r.m_bRequestSuccessful || codigo == 0 || codigo >= 500 || codigo == 429)
                {
                    Plugin.Log.LogInfo($"[Métricas] Sem conexão para enviar {nome} (código {codigo}); tento de novo mais tarde.");
                    Fila.Clear();
                    _proximaTentativa = Time.unscaledTime + 600f;
                }
                else if (codigo >= 200 && codigo < 300 || codigo == 409)
                {
                    // 409: esse resumo já tinha chegado antes.
                    if (arquivo != null && File.Exists(arquivo)) File.Delete(arquivo);
                    Plugin.Log.LogInfo($"[Métricas] Resumo {nome} enviado. Obrigado por ajudar no balanceamento!");
                    _proximaTentativa = Time.unscaledTime + 2f;
                }
                else
                {
                    // 400/403: o banco recusou o formato (versão antiga do mod, por exemplo). Não insiste.
                    if (arquivo != null && File.Exists(arquivo))
                    {
                        Directory.CreateDirectory(Recusadas);
                        string destino = Path.Combine(Recusadas, nome);
                        if (File.Exists(destino)) File.Delete(destino);
                        File.Move(arquivo, destino);
                    }
                    Plugin.Log.LogWarning($"[Métricas] O servidor recusou {nome} (código {codigo}); o arquivo foi para metricas/recusadas.");
                    _proximaTentativa = Time.unscaledTime + 2f;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Métricas] Resposta do envio: {(e.InnerException ?? e).Message}");
            }
            finally
            {
                SteamHTTP.ReleaseHTTPRequest(r.m_hRequest);
            }
        }
    }
}
