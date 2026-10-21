using UnityEngine;
using UnityEngine.SceneManagement;

// Menu inicial: Principal -> Personagem -> Mapa -> jogo. Cada etapa é um painel do Canvas.
// A escolha fica em campos estáticos, que o MapaGerador lê ao gerar a fase.
public class MenuInicial : MonoBehaviour
{
    [Header("Painéis")]
    [SerializeField] private GameObject painelPrincipal;
    [SerializeField] private GameObject painelPersonagem;
    [SerializeField] private GameObject painelMapa;

    [Header("Opções")]
    [SerializeField] private PersonagemData[] personagens;
    [Tooltip("Pixel arts dos mapas, na mesma ordem dos botões.")]
    [SerializeField] private Texture2D[] mapas;
    [Tooltip("Mapa pequeno do tutorial")]
    [SerializeField] private Texture2D mapaTutorial;

    [Header("Cena")]
    [SerializeField] private string nomeCenaJogo = "SampleScene";

    // Escolhas da sessão atual (nulo = o MapaGerador usa o que está configurado nele)
    public static PersonagemData PersonagemEscolhido { get; private set; }
    public static Texture2D MapaEscolhido { get; private set; }
    public static bool Tutorial { get; private set; } // aberto pelo botão Tutorial

    private void Start()
    {
        // vindo do jogo: destrava o tempo e solta o cursor (que estava preso no centro) pra clicar nos botões
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        MostrarPainel(painelPrincipal);
    }

    public void Jogar() => MostrarPainel(painelPersonagem);

    public void Voltar()
    {
        // do mapa volta pra personagem; de personagem volta pro principal
        MostrarPainel(painelMapa.activeSelf ? painelPersonagem : painelPrincipal);
    }

    public void Sair()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public void EscolherPersonagem(int indice)
    {
        PersonagemData escolhido = indice >= 0 && indice < personagens.Length ? personagens[indice] : null;
        if (escolhido == null || escolhido.prefab == null)
        {
            Debug.LogWarning("MenuInicial: personagem sem prefab, ainda não disponível.");
            return;
        }

        PersonagemEscolhido = escolhido;
        MostrarPainel(painelMapa);
    }

    public void EscolherMapa(int indice)
    {
        if (indice < 0 || indice >= mapas.Length || mapas[indice] == null)
        {
            Debug.LogWarning("MenuInicial: mapa não configurado.");
            return;
        }

        MapaEscolhido = mapas[indice];
        Tutorial = false;
        SceneManager.LoadScene(nomeCenaJogo);
    }

    // Tutorial: mapa pequeno, sempre com o primeiro personagem (Atirador)
    public void ComecarTutorial()
    {
        PersonagemEscolhido = personagens[0];
        MapaEscolhido = mapaTutorial;
        Tutorial = true;
        SceneManager.LoadScene(nomeCenaJogo);
    }

    private void MostrarPainel(GameObject painel)
    {
        painelPrincipal.SetActive(painel == painelPrincipal);
        painelPersonagem.SetActive(painel == painelPersonagem);
        painelMapa.SetActive(painel == painelMapa);
    }
}
