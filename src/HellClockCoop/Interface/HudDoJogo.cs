namespace HellClockCoop.Interface
{
    /// <summary>
    /// A HUD do jogo sem busca na cena (0.30.3). GenericHUD.Instance é Guaranteed (criaria uma HUD) e Singleton&lt;T&gt;.Instance
    /// procura na cena quando o registro está vazio. Aqui só se lê o registro que a HUD faz no Awake (Singleton.SayHello):
    /// IsValid só lê o registro; GetInstance, chamado só depois dele, devolve a referência sem procurar. Sem o IsValid
    /// antes, o GetInstance com o registro vazio varreria as raízes de todas as cenas: o teste não pode sair.
    /// Nula enquanto a HUD não acordou (menu do jogo, carregamento).
    /// Até a 0.30.2 a HUD era achada com Resources.FindObjectsOfTypeAll, que em jogo custa de 50 a 90 ms por busca.
    /// </summary>
    internal static class HudDoJogo
    {
        internal static GenericHUD Atual() => Singleton<GenericHUD>.IsValid ? Singleton<GenericHUD>.GetInstance() : null;
    }
}
