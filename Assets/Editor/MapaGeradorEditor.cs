using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapaGerador))]
public class MapaGeradorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        var gerador = (MapaGerador)target;

        if (GUILayout.Button("Gerar Preview no Editor", GUILayout.Height(30)))
        {
            Undo.RegisterFullObjectHierarchyUndo(gerador.gameObject, "Gerar Preview do Mapa");
            gerador.GerarPreviewNoEditor();
            EditorUtility.SetDirty(gerador);
        }

        if (GUILayout.Button("Limpar Mapa"))
        {
            Undo.RegisterFullObjectHierarchyUndo(gerador.gameObject, "Limpar Mapa");
            gerador.LimparMapa();
            EditorUtility.SetDirty(gerador);
        }
    }
}
