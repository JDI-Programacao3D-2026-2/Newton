using UnityEngine;

// Um asset por personagem jogável (botão direito > Create > Personagens > Personagem).
[CreateAssetMenu(fileName = "NovoPersonagem", menuName = "Personagens/Personagem")]
public class PersonagemData : ScriptableObject
{
    public string nome;

    [Tooltip("Prefab do player que o MapaGerador instancia. Vazio = personagem ainda não disponível.")]
    public GameObject prefab;
}
