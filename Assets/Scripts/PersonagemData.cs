using UnityEngine;

// Um asset por personagem jogável (botão direito > Create > Personagens > Personagem).
[CreateAssetMenu(fileName = "NovoPersonagem", menuName = "Personagens/Personagem")]
public class PersonagemData : ScriptableObject
{
    [Header("Informações")]
    public string nome;
    [TextArea] public string descricao;
    public Sprite retrato;

    [Header("Gameplay")]
    [Tooltip("Prefab do player que o MapaGerador instancia. Vazio = personagem ainda não disponível.")]
    public GameObject prefab;
}
