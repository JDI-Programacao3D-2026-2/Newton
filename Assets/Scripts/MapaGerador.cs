using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

// Le a pixel art de Assets/Art/Mapa/ e monta o mapa com os prefabs configurados no Inspector.
// Cor do inimigo/player parecida com o piso ao redor = nasce "escondido" ali.
// Botão "Gerar Preview no Editor" mostra o resultado sem precisar dar Play.
// Depois de gerar, constrói o NavMesh e só então coloca o player e os inimigos.

public class MapaGerador : MonoBehaviour
{
    [Header("Pixel Art do Mapa")]
    [SerializeField] Texture2D imagemDoMapa;
    [Tooltip("Tamanho de 1 pixel no mundo, em metros")]
    [SerializeField] float tamanhoDoTile = 2f;

    [Header("Tiles")]
    [SerializeField] TileDefinicao grama   = new TileDefinicao { nome = "Grama",   altura = 0.2f };
    [SerializeField] TileDefinicao rua     = new TileDefinicao { nome = "Rua",     altura = 0.2f };
    [SerializeField] TileDefinicao calcada = new TileDefinicao { nome = "Calçada", altura = 0.35f };
    [SerializeField] TileDefinicao predio  = new TileDefinicao { nome = "Prédio",  altura = 6f };
    [SerializeField] TileDefinicao chao    = new TileDefinicao { nome = "Chão",    altura = 0.2f };
    [SerializeField] TileDefinicao arvore  = new TileDefinicao { nome = "Árvore",  redimensionar = false };

    [Header("Inimigos (pontos de nascimento, colocados à mão por vocês)")]
    [SerializeField] List<InimigoDefinicao> inimigos = new List<InimigoDefinicao>();

    [Header("Player")]
    [SerializeField] SpawnDefinicao player;

    [Header("Navegação")]
    [Tooltip("Assa o NavMesh do mapa gerado (necessário pros inimigos andarem).")]
    [SerializeField] bool construirNavMesh = true;

    [Header("Evento apos gerar")]
    public UnityEvent aoTerminarDeGerar;

    // O player instanciado no spawn, disponível depois que aoTerminarDeGerar dispara.
    public GameObject PlayerInstanciado { get; private set; }

    const int ToleranciaCor = 900;
    const float SegundosPorFrame = 0.008f;
    // Acima dessa altura o objeto não é andável (prédio, árvore)
    const float AlturaMaximaAndavel = 0.6f;

    float inicioDoFrame;
    readonly List<GameObject> _inimigosGerados = new List<GameObject>();

    void Start()
    {
        StartCoroutine(GerarMapaInterno(instantaneo: false));
    }

    // Gera o mapa na hora, sem pausas por frame — pra ver o resultado no Editor sem apertar Play.
    public void GerarPreviewNoEditor()
    {
        var rotina = GerarMapaInterno(instantaneo: true);
        while (rotina.MoveNext()) { }
    }

    public void LimparMapa()
    {
        var surface = GetComponent<NavMeshSurface>();
        if (surface != null)
        {
            surface.RemoveData();
            surface.navMeshData = null;
        }

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject filho = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(filho);
            else DestroyImmediate(filho);
        }

        foreach (GameObject inimigo in _inimigosGerados)
        {
            if (inimigo == null) continue;
            if (Application.isPlaying) Destroy(inimigo);
            else DestroyImmediate(inimigo);
        }
        _inimigosGerados.Clear();

        // O player não é filho do MapaGerador (não pode entrar na combinação de meshes), então
        // limpa à parte. Remove qualquer Player solto na cena por tag, não só pela referência
        // guardada — se o preview do Editor sobreviver ao entrar em Play (depende da config de
        // Enter Play Mode Settings da Unity), a referência antiga ainda é válida, mas é mais
        // seguro garantir que não sobra nenhum player duplicado de qualquer jeito.
        foreach (GameObject p in GameObject.FindGameObjectsWithTag("Player"))
        {
            if (Application.isPlaying) Destroy(p);
            else DestroyImmediate(p);
        }
        PlayerInstanciado = null;
    }

    IEnumerator GerarMapaInterno(bool instantaneo)
    {
        LimparMapa();

        if (imagemDoMapa == null)
        {
            Debug.LogError("MapaGerador: atribua a pixel art no Inspector.");
            yield break;
        }
        if (!imagemDoMapa.isReadable)
        {
            Debug.LogError($"MapaGerador: a imagem '{imagemDoMapa.name}' não está legível. Coloque-a em Assets/Art/Mapa/ e clique com o botão direito > Reimport.");
            yield break;
        }

        TileDefinicao[] tiles = TilesValidos();
        List<InimigoDefinicao> inimigosValidos = InimigosValidos();

        int largura = imagemDoMapa.width;
        int altura  = imagemDoMapa.height;
        Color32[] pixels = imagemDoMapa.GetPixels32();

        bool playerValido = player != null && player.prefab != null &&
            !(player.cor.r == 0 && player.cor.g == 0 && player.cor.b == 0 && player.cor.a == 0);

        // Camada de chão (vira retângulos), árvores (um por pixel) e inimigos (um por pixel)
        // Guarda também qual piso nasceu embaixo de cada ponto, pra apoiar o objeto na altura certa dele
        TileDefinicao[] camadaChao = new TileDefinicao[largura * altura];
        var arvores = new List<(int x, int y, TileDefinicao tile, TileDefinicao piso, TileDefinicao contexto)>();
        var inimigosAGerar = new List<(int x, int y, InimigoDefinicao inimigo, TileDefinicao piso)>();
        Vector2Int? posicaoPlayer = null;
        TileDefinicao pisoDoPlayer = null;
        var coresSemTile = new Dictionary<string, int>();

        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].a < 10) continue;

            int x = i % largura;
            int y = i / largura;
            Color32 cor = pixels[i];

            if (playerValido && posicaoPlayer == null && DiferencaCor(cor, player.cor) < ToleranciaCor)
            {
                posicaoPlayer = new Vector2Int(x, y);
                pisoDoPlayer = ResolverPiso(player.piso, x, y, largura, altura, pixels);
                if (pisoDoPlayer != null && pisoDoPlayer.prefab != null) camadaChao[i] = pisoDoPlayer;
                continue;
            }

            InimigoDefinicao inimigo = EncontrarInimigo(cor, inimigosValidos);
            if (inimigo != null)
            {
                TileDefinicao piso = ResolverPiso(inimigo.piso, x, y, largura, altura, pixels);
                inimigosAGerar.Add((x, y, inimigo, piso));
                if (piso != null && piso.prefab != null) camadaChao[i] = piso;
                continue;
            }

            TileDefinicao tile = EncontrarTile(cor, tiles);
            if (tile == null)
            {
                // Cor opaca que não é de nenhum tile: vira um buraco no mapa. Conta pra avisar depois.
                string chave = $"({cor.r}, {cor.g}, {cor.b})";
                coresSemTile.TryGetValue(chave, out int n);
                coresSemTile[chave] = n + 1;
                continue;
            }

            if (tile.redimensionar)
            {
                camadaChao[i] = tile;
            }
            else
            {
                TileDefinicao contexto = PisoDoContexto(x, y, largura, altura, pixels);
                arvores.Add((x, y, tile, grama, contexto));
                if (grama.prefab != null) camadaChao[i] = grama;
            }
        }

        AvisarProblemasDeCor(tiles, inimigosValidos, playerValido, coresSemTile, inimigosAGerar.Count, posicaoPlayer.HasValue);

        bool[] usado = new bool[largura * altura];
        int total = 0;
        inicioDoFrame = Time.realtimeSinceStartup;

        for (int y = 0; y < altura; y++)
        {
            for (int x = 0; x < largura; x++)
            {
                int i = y * largura + x;
                TileDefinicao tile = camadaChao[i];
                if (tile == null || usado[i]) continue;

                // Estica para a direita enquanto a cor for igual
                int w = 1;
                while (x + w < largura && !usado[i + w] && camadaChao[i + w] == tile) w++;

                // Estica para baixo enquanto a linha inteira for igual
                int h = 1;
                while (y + h < altura && LinhaIgual(camadaChao, usado, largura, x, y + h, w, tile)) h++;

                for (int dy = 0; dy < h; dy++)
                    for (int dx = 0; dx < w; dx++)
                        usado[(y + dy) * largura + x + dx] = true;

                GameObject obj = Instantiate(tile.prefab, transform);
                Vector3 tamanho = new Vector3(w * tamanhoDoTile, tile.altura, h * tamanhoDoTile);
                Encaixar(obj, CentroDaArea(x, y, w, h), tamanho, tile.offsetY);
                MarcarNaoAndavelSeAlto(obj);
                total++;

                if (!instantaneo && PassouDoTempo()) { yield return null; inicioDoFrame = Time.realtimeSinceStartup; }
            }
        }

        foreach (var (x, y, tile, piso, contexto) in arvores)
        {
            GameObject obj = Instantiate(tile.prefab, transform);
            float baseY = AlturaDoPiso(piso);
            Encaixar(obj, CentroDaArea(x, y, 1, 1), Vector3.zero, baseY);
            MarcarNaoAndavelSeAlto(obj);
            AdicionarPreenchimentoDeColisao(x, y, baseY, AlturaDoPiso(contexto));
            total++;

            if (!instantaneo && PassouDoTempo()) { yield return null; inicioDoFrame = Time.realtimeSinceStartup; }
        }

        // NavMesh só do mapa (também no preview, pra ver a área andável na Scene)
        if (construirNavMesh)
        {
            // se o NavMesh falhar, player e inimigos ainda nascem
            try { ConstruirNavMesh(); }
            catch (System.Exception e) { Debug.LogError($"MapaGerador: falha ao construir o NavMesh: {e.Message}"); }
        }

        // Combinar meshes só faz sentido em runtime de verdade, não no preview do Editor
        if (Application.isPlaying) StaticBatchingUtility.Combine(gameObject);


        // No preview os inimigos aparecem só pra mostrar onde nascem
        if (instantaneo)
        {
            foreach (var (x, y, inimigo, piso) in inimigosAGerar)
            {
                GameObject obj = Instantiate(inimigo.prefab, transform);
                Encaixar(obj, CentroDaArea(x, y, 1, 1), Vector3.zero, AlturaDoPiso(piso));
                total++;
            }
        }

        // Player fica fora da combinação acima (ele se move, não pode ser "assado" junto com o mapa)
        if (posicaoPlayer.HasValue)
        {
            PlayerInstanciado = Instantiate(player.prefab);

            // Desliga o CharacterController antes de teleportar e religa depois — mover o
            // Transform direto com o controller ligado deixa o estado interno dele (chão,
            // colisão) inconsistente de vez em quando, causando esse "nasce fora do mapa"
            // só em algumas rodadas.
            CharacterController controller = PlayerInstanciado.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            Encaixar(PlayerInstanciado, CentroDaArea(posicaoPlayer.Value.x, posicaoPlayer.Value.y, 1, 1), Vector3.zero, AlturaDoPiso(pisoDoPlayer));

            if (controller != null) controller.enabled = true;

            // zera a velocidade do Rigidbody
            if (PlayerInstanciado.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
        }
        else if (playerValido)
        {
            Debug.LogWarning("MapaGerador: nenhum pixel com a cor do Player foi encontrado na pixel art.");
        }

        // Inimigos por último: precisam do NavMesh pronto e do player na cena
        if (!instantaneo)
        {
            foreach (var (x, y, inimigo, piso) in inimigosAGerar)
            {
                GerarInimigo(x, y, inimigo, piso);
                total++;
            }
        }

        Debug.Log($"MapaGerador: mapa {largura}x{altura} gerado com {total} objetos.");
        aoTerminarDeGerar?.Invoke();
    }

    void ConstruirNavMesh()
    {
        Physics.SyncTransforms();

        var surface = GetComponent<NavMeshSurface>();
        if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();

        surface.collectObjects = CollectObjects.Children;
        // Usa as malhas visíveis: a malha do MeshCollider dos prefabs do ProBuilder é recriada depois
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;

        // mesmo tipo de agente do inimigo
        foreach (InimigoDefinicao inimigo in inimigos)
        {
            if (inimigo != null && inimigo.prefab != null && inimigo.prefab.TryGetComponent(out NavMeshAgent agente))
            {
                surface.agentTypeID = agente.agentTypeID;
                break;
            }
        }

        // tiles grandes reduzem as emendas no meio das áreas
        surface.overrideVoxelSize = false;
        surface.minRegionArea = 2f;
        surface.overrideTileSize = true;
        surface.tileSize = 512;
        surface.BuildNavMesh();
    }

    void GerarInimigo(int x, int y, InimigoDefinicao inimigo, TileDefinicao piso)
    {
        // nasce 1 m acima do piso pra o agent achar o NavMesh
        Vector3 centro = CentroDaArea(x, y, 1, 1);
        GameObject obj = Instantiate(inimigo.prefab, centro + Vector3.up * (AlturaDoPiso(piso) + 1f), Quaternion.identity);
        _inimigosGerados.Add(obj);

        Encaixar(obj, centro, Vector3.zero, AlturaDoPiso(piso));

        if (obj.TryGetComponent(out NavMeshAgent agente) && agente.enabled)
        {
            if (NavMesh.SamplePosition(obj.transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                agente.Warp(hit.position);
            else
                Debug.LogWarning($"MapaGerador: o inimigo no pixel ({x},{y}) ficou fora do NavMesh.");
        }
    }

    // Objetos altos ficam fora do NavMesh
    static void MarcarNaoAndavelSeAlto(GameObject obj)
    {
        if (!CalcularBounds(obj, out Bounds b) || b.max.y <= AlturaMaximaAndavel) return;

        var modificador = obj.AddComponent<NavMeshModifier>();
        modificador.overrideArea = true;
        modificador.area = 1; // Not Walkable
    }

    // Avisa de cores configuradas errado
    void AvisarProblemasDeCor(TileDefinicao[] tiles, List<InimigoDefinicao> inimigosValidos, bool playerValido,
        Dictionary<string, int> coresSemTile, int pontosDeInimigo, bool achouPlayer)
    {
        if (playerValido)
        {
            foreach (TileDefinicao tile in tiles)
                if (DiferencaCor(player.cor, tile.cor) < ToleranciaCor)
                    Debug.LogWarning($"MapaGerador: a cor do Player é parecida com a do tile '{Nome(tile.nome, tile.prefab)}'.");
        }

        foreach (InimigoDefinicao inimigo in inimigosValidos)
            foreach (TileDefinicao tile in tiles)
                if (DiferencaCor(inimigo.cor, tile.cor) < ToleranciaCor)
                    Debug.LogWarning($"MapaGerador: a cor do inimigo '{Nome(inimigo.nome, inimigo.prefab)}' é parecida com a do tile '{Nome(tile.nome, tile.prefab)}'.");

        if (inimigosValidos.Count > 0 && pontosDeInimigo == 0)
            Debug.LogWarning("MapaGerador: nenhum pixel da imagem tem a cor de um inimigo.");

        if (playerValido && !achouPlayer)
            Debug.LogWarning("MapaGerador: nenhum pixel da imagem tem a cor do Player.");

        foreach (var par in coresSemTile)
            Debug.LogWarning($"MapaGerador: {par.Value} pixel(s) com a cor {par.Key} sem tile, inimigo ou player (viram buraco no mapa).");
    }

    bool PassouDoTempo()
    {
        return Time.realtimeSinceStartup - inicioDoFrame > SegundosPorFrame;
    }

    static bool LinhaIgual(TileDefinicao[] camada, bool[] usado, int largura, int x, int y, int w, TileDefinicao tile)
    {
        for (int dx = 0; dx < w; dx++)
        {
            int i = y * largura + x + dx;
            if (usado[i] || camada[i] != tile) return false;
        }
        return true;
    }

    Vector3 CentroDaArea(int x, int y, int w, int h)
    {
        return transform.position + new Vector3((x + w / 2f) * tamanhoDoTile, 0f, (y + h / 2f) * tamanhoDoTile);
    }

    // Redimensiona pelo tamanho real do objeto e o apoia no chão, independente de onde está o pivô.
    // Componentes do tamanhoAlvo <= 0 mantêm o tamanho original naquele eixo.
    static void Encaixar(GameObject obj, Vector3 centro, Vector3 tamanhoAlvo, float baseY)
    {
        if (!CalcularBounds(obj, out Bounds b))
        {
            obj.transform.position = new Vector3(centro.x, centro.y + baseY, centro.z);
            return;
        }

        Vector3 escala = obj.transform.localScale;
        if (tamanhoAlvo.x > 0f && b.size.x > 0.0001f) escala.x *= tamanhoAlvo.x / b.size.x;
        if (tamanhoAlvo.y > 0f && b.size.y > 0.0001f) escala.y *= tamanhoAlvo.y / b.size.y;
        if (tamanhoAlvo.z > 0f && b.size.z > 0.0001f) escala.z *= tamanhoAlvo.z / b.size.z;
        obj.transform.localScale = escala;

        CalcularBounds(obj, out b);
        obj.transform.position += new Vector3(
            centro.x - b.center.x,
            centro.y + baseY - b.min.y,
            centro.z - b.center.z
        );

        SincronizarColliders(obj);
    }

    // Prefabs do ProBuilder guardam a malha num componente próprio e recriam ela sozinhos
    // ao nascer, mas o MeshCollider às vezes fica apontando pra malha antiga/vazia. Aqui
    // forço o collider a usar a mesma malha que está sendo desenhada de verdade.
    static void SincronizarColliders(GameObject obj)
    {
        foreach (MeshCollider colisor in obj.GetComponentsInChildren<MeshCollider>())
        {
            MeshFilter filtro = colisor.GetComponent<MeshFilter>();
            if (filtro != null && filtro.sharedMesh != null)
                colisor.sharedMesh = filtro.sharedMesh;
        }
    }

    // Preenche, só na colisão (sem nada visível), a diferença entre a altura visual da
    // árvore (sempre grama) e a altura real do que está ao redor dela na pixel art.
    // Só cria o preenchimento quando faz diferença — árvore cercada de grama normal não
    // ganha nada, só a que forma uma ilha (cercada de algo mais alto, tipo calçada).
    void AdicionarPreenchimentoDeColisao(int x, int y, float alturaBase, float alturaContexto)
    {
        float alturaPreenchimento = alturaContexto - alturaBase;
        if (alturaPreenchimento <= 0.001f) return;

        GameObject preenchimento = new GameObject("ColisaoInvisivel");
        preenchimento.transform.SetParent(transform);

        Vector3 centro = CentroDaArea(x, y, 1, 1);
        preenchimento.transform.position = new Vector3(centro.x, alturaBase + alturaPreenchimento / 2f, centro.z);

        BoxCollider caixa = preenchimento.AddComponent<BoxCollider>();
        caixa.size = new Vector3(tamanhoDoTile, alturaPreenchimento, tamanhoDoTile);
    }

    static bool CalcularBounds(GameObject obj, out Bounds bounds)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        bounds = default;
        if (renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    // Altura da superfície de cima do piso, pra apoiar árvore/inimigo/player em cima dele, não na base
    static float AlturaDoPiso(TileDefinicao piso)
    {
        return piso != null ? piso.offsetY + piso.altura : 0f;
    }

    TileDefinicao ResolverPiso(PisoForcado escolha, int x, int y, int largura, int altura, Color32[] pixels)
    {
        switch (escolha)
        {
            case PisoForcado.Grama:   return grama;
            case PisoForcado.Rua:     return rua;
            case PisoForcado.Calcada: return calcada;
            case PisoForcado.Chao:    return chao;
            default:                  return PisoDoContexto(x, y, largura, altura, pixels);
        }
    }

    // O piso embaixo do inimigo é o mais comum entre os 8 pixels vizinhos na pixel art (incluindo diagonais).
    // Assim ele nasce disfarçado no piso que já está ao redor dele (rua, calçada, grama...).
    // Prédio não entra na votação — não faz sentido o inimigo "parecer" prédio.
    TileDefinicao PisoDoContexto(int x, int y, int largura, int altura, Color32[] pixels)
    {
        TileDefinicao[] pisos = { grama, rua, calcada, chao };
        var votos = new Dictionary<TileDefinicao, int>();

        int[] dx = { -1, 1, 0, 0, -1, -1, 1, 1 };
        int[] dy = { 0, 0, -1, 1, -1, 1, -1, 1 };

        for (int d = 0; d < 8; d++)
        {
            int nx = x + dx[d];
            int ny = y + dy[d];
            if (nx < 0 || nx >= largura || ny < 0 || ny >= altura) continue;

            Color32 corVizinho = pixels[ny * largura + nx];
            TileDefinicao pisoVizinho = EncontrarTile(corVizinho, pisos);
            if (pisoVizinho == null) continue;

            votos.TryGetValue(pisoVizinho, out int atual);
            votos[pisoVizinho] = atual + 1;
        }

        TileDefinicao melhor = null;
        int maisVotado = 0;
        foreach (var par in votos)
        {
            if (par.Value > maisVotado)
            {
                maisVotado = par.Value;
                melhor = par.Key;
            }
        }

        return melhor ?? chao;
    }

    TileDefinicao[] TilesValidos()
    {
        var todos = new[] { grama, rua, calcada, predio, chao, arvore };
        var validos = new List<TileDefinicao>();

        foreach (TileDefinicao tile in todos)
        {
            if (tile == null || tile.prefab == null) continue;

            Color32 c = tile.cor;
            if (c.r == 0 && c.g == 0 && c.b == 0 && c.a == 0)
            {
                Debug.LogWarning($"MapaGerador: '{Nome(tile.nome, tile.prefab)}' tem prefab mas a cor não foi configurada. Tile ignorado.");
                continue;
            }

            foreach (TileDefinicao outro in validos)
            {
                if (DiferencaCor(tile.cor, outro.cor) < ToleranciaCor)
                    Debug.LogWarning($"MapaGerador: '{Nome(tile.nome, tile.prefab)}' e '{Nome(outro.nome, outro.prefab)}' têm cores iguais ou muito parecidas. Só '{Nome(outro.nome, outro.prefab)}' será usado.");
            }

            validos.Add(tile);
        }

        return validos.ToArray();
    }

    List<InimigoDefinicao> InimigosValidos()
    {
        var validos = new List<InimigoDefinicao>();

        foreach (InimigoDefinicao inimigo in inimigos)
        {
            if (inimigo == null || inimigo.prefab == null) continue;

            Color32 c = inimigo.cor;
            if (c.r == 0 && c.g == 0 && c.b == 0 && c.a == 0)
            {
                Debug.LogWarning($"MapaGerador: inimigo '{Nome(inimigo.nome, inimigo.prefab)}' tem prefab mas a cor não foi configurada. Ignorado.");
                continue;
            }

            validos.Add(inimigo);
        }

        return validos;
    }

    static string Nome(string nome, GameObject prefab)
    {
        return string.IsNullOrEmpty(nome) ? prefab.name : nome;
    }

    static TileDefinicao EncontrarTile(Color32 cor, TileDefinicao[] tiles)
    {
        TileDefinicao melhor = null;
        int menorDist = ToleranciaCor;

        foreach (TileDefinicao tile in tiles)
        {
            int dist = DiferencaCor(cor, tile.cor);
            if (dist < menorDist)
            {
                menorDist = dist;
                melhor = tile;
            }
        }

        return melhor;
    }

    static InimigoDefinicao EncontrarInimigo(Color32 cor, List<InimigoDefinicao> inimigos)
    {
        InimigoDefinicao melhor = null;
        int menorDist = ToleranciaCor;

        foreach (InimigoDefinicao inimigo in inimigos)
        {
            int dist = DiferencaCor(cor, inimigo.cor);
            if (dist < menorDist)
            {
                menorDist = dist;
                melhor = inimigo;
            }
        }

        return melhor;
    }

    static int DiferencaCor(Color32 a, Color32 b)
    {
        int dr = a.r - b.r;
        int dg = a.g - b.g;
        int db = a.b - b.b;
        return dr * dr + dg * dg + db * db;
    }
}

[System.Serializable]
public class TileDefinicao
{
    public string nome;
    public Color32 cor;
    public GameObject prefab;
    [Tooltip("Altura da base no eixo Y (0 = nível do chão)")]
    public float offsetY = 0f;
    [Tooltip("Altura final em metros (ex: rua 0.2, calçada 0.35, prédio 6). 0 = mantém a altura do prefab. Só vale para tiles redimensionados.")]
    public float altura = 0f;
    [Tooltip("Redimensiona para cobrir a área (grama, rua, prédio...). Desative para objetos individuais como árvores.")]
    public bool redimensionar = true;
}

public enum PisoForcado { Automatico, Grama, Rua, Calcada, Chao }

[System.Serializable]
public class InimigoDefinicao
{
    public string nome;
    public Color32 cor;
    public GameObject prefab;
    [Tooltip("Automático adivinha pelos pixels ao redor. Force um piso se ele adivinhar errado (ex: cruzamentos de rua).")]
    public PisoForcado piso = PisoForcado.Automatico;
}

[System.Serializable]
public class SpawnDefinicao
{
    public Color32 cor;
    public GameObject prefab;
    [Tooltip("Automático adivinha pelos pixels ao redor. Force um piso se ele adivinhar errado (ex: cruzamentos de rua).")]
    public PisoForcado piso = PisoForcado.Automatico;
}
