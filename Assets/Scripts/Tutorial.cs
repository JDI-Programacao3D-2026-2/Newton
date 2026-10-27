using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// tutorial: uma lista de tarefas na tela. cada item é marcado quando o player faz a ação.
// só aparece quando o jogo foi aberto pelo botão "Tutorial" do menu
public class Tutorial : MonoBehaviour
{
    [SerializeField] private GameObject painel;  // fundo da lista (começa escondido)
    [SerializeField] private Text textoLista;    // texto com as tarefas

    private readonly string[] tarefas =
    {
        "Mover (WASD)",
        "Pular (Espaco)",
        "Dash (Shift)",
        "Atacar (clique)",
        "Habilidade Q",
        "Habilidade E",
        "Placar (Tab)"
    };

    private bool[] feito;

    private void Start()
    {
        feito = new bool[tarefas.Length];

        // fora do modo tutorial a lista nem aparece
        enabled = MenuInicial.Tutorial;
        painel.SetActive(MenuInicial.Tutorial);
        AtualizarTexto();
    }

    private void Update()
    {
        Keyboard teclado = Keyboard.current;
        if (teclado == null || Mouse.current == null) return;

        if (teclado.wKey.isPressed || teclado.aKey.isPressed || teclado.sKey.isPressed || teclado.dKey.isPressed) Marcar(0);
        if (teclado.spaceKey.wasPressedThisFrame) Marcar(1);
        if (teclado.leftShiftKey.wasPressedThisFrame) Marcar(2);
        if (Mouse.current.leftButton.wasPressedThisFrame) Marcar(3);
        if (teclado.qKey.wasPressedThisFrame) Marcar(4);
        if (teclado.eKey.wasPressedThisFrame) Marcar(5);
        if (teclado.tabKey.isPressed) Marcar(6);
    }

    private void Marcar(int indice)
    {
        if (feito[indice]) return;

        feito[indice] = true;
        AtualizarTexto();
    }

    private void AtualizarTexto()
    {
        string texto = "TUTORIAL\n";
        int total = 0;

        for (int i = 0; i < tarefas.Length; i++)
        {
            texto += (feito[i] ? "[X] " : "[ ] ") + tarefas[i] + "\n";
            if (feito[i]) total++;
        }

        if (total == tarefas.Length)
            texto += "\nConcluido! Derrote os inimigos.";

        texto += "\nEsc = sair";

        textoLista.text = texto;
    }
}
