namespace HellClockCoop.Balanceamento
{
    /// <summary>
    /// Destino do envio opcional das métricas (projeto Firebase só deste mod, sem relação com nenhum outro).
    /// Os valores reais ficam em EnvioConfig.local.cs, que NÃO vai para o repositório (.gitignore).
    /// Sem esse arquivo o mod compila normalmente e o envio fica desligado.
    /// </summary>
    internal static partial class EnvioConfig
    {
        public static readonly string Projeto;
        public static readonly string Chave;

        static EnvioConfig()
        {
            string projeto = null, chave = null;
            Carregar(ref projeto, ref chave);
            Projeto = projeto;
            Chave = chave;
        }

        static partial void Carregar(ref string projeto, ref string chave);

        public static bool Configurado => !string.IsNullOrEmpty(Projeto) && !string.IsNullOrEmpty(Chave);
    }
}
