using UnityEngine;

public class CalculadoraDano : MonoBehaviour
{
   
    int ataque = 25;
    int defesa = 10;
    float multiplicador = 1.5f;

  
    float vida = 100f;

    void Start()
    {
        

        float danoReal = (ataque - defesa) * multiplicador;

        vida -= danoReal;

        Debug.Log(
            $"=== Turno 1 === " +
            $"Ataque: {ataque} | " +
            $"Defesa: {defesa} | " +
            $"Dano real: {danoReal} | " +
            $"{(danoReal > 20 ? "Dano crítico!" : "Dano normal")} " +
            $"Vida restante: {vida}"
        );



        danoReal = (ataque - defesa) * multiplicador;

        vida -= danoReal;

        Debug.Log(
            $"=== Turno 2 === " +
            $"Ataque: {ataque} | " +
            $"Defesa: {defesa} | " +
            $"Dano real: {danoReal} | " +
            $"{(danoReal > 20 ? "Dano crítico!" : "Dano normal")} " +
            $"Vida restante: {vida}"
        );


   

        danoReal = (ataque - defesa) * multiplicador;

        vida -= danoReal;

        Debug.Log(
            $"=== Turno 3 === " +
            $"Ataque: {ataque} | " +
            $"Defesa: {defesa} | " +
            $"Dano real: {danoReal} | " +
            $"{(danoReal > 20 ? "Dano crítico!" : "Dano normal")} " +
            $"Vida restante: {vida}"
        );
    }
}
