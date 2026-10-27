using UnityEngine;
using UnityEngine.SceneManagement;

// menu inicial: principal -> personagem -> mapa -> jogo. cada etapa é um painel do Canvas
// a escolha fica em campos estáticos, que o MapaGerador lê quando gera a fase
public class MenuInicial : MonoBehaviour
{
    [Header("Painéis")]
    [SerializeField] private GameObject painelPrincipal;
    [SerializeField] private GameObject painelPersonagem;
    [SerializeField] private GameObject painelMapa;
    [SerializeField] private GameObject painelControles;

    [Header("Opções")]
    [SerializeField] private PersonagemData[] personagens;
    [Tooltip("Pixel arts dos mapas, na mesma ordem dos botões.")]
    [SerializeField] private Texture2D[] mapas;
    [Tooltip("Mapa pequeno do tutorial")]
    [SerializeField] private Texture2D mapaTutorial;

    [Header("Cena")]
    [SerializeField] private string nomeCenaJogo = "SampleScene";

    // escolhas dessa sessão (nulo = o MapaGerador usa o que tá configurado nele)
    public static PersonagemData PersonagemEscolhido { get; private set; }
    public static Texture2D MapaEscolhido { get; private set; }
    public static bool Tutorial { get; private set; } // aberto pelo botão Tutorial

    private void Start()
    {
        // vindo do jogo: destrava o tempo e solta o cursor (que tava preso no centro) pra clicar nos botões
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        MostrarPainel(painelPrincipal);
    }

    public void Jogar() => MostrarPainel(painelPersonagem);

    public void Controles() => MostrarPainel(painelControles);

    public void Voltar()
    {
        // do mapa volta pra personagem, dos outros painéis volta pro principal
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
        if (personagens[indice].prefab == null)
        {
            Debug.LogWarning("MenuInicial: personagem sem prefab, ainda não disponível.");
            return;
        }

        PersonagemEscolhido = personagens[indice];
        MostrarPainel(painelMapa);
    }

    public void EscolherMapa(int indice)
    {
        MapaEscolhido = mapas[indice];
        Tutorial = false;
        SceneManager.LoadScene(nomeCenaJogo);
    }

    // tutorial: mapa pequeno, sempre com o primeiro personagem (Atirador)
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
        painelControles.SetActive(painel == painelControles);
    }
}
