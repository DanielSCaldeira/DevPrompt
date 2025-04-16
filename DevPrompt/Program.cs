using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

try
{
    var configuracao = CarregarConfiguracao();
    string baseDir = AppContext.BaseDirectory;
    string diretorioSolucao = Path.GetFullPath(Path.Combine(baseDir, configuracao["AppSettings:SolutionDirectory"] ?? throw new InvalidOperationException("SolutionDirectory is not configured.")));

    var (sql, nomeAssembly, nomeNamespace) = LerEntradaUsuario();
    var (esquema, tabela) = ObterEsquemaETabela(sql);
    var (conteudoXml, conteudoModel, conteudoService, conteudoDto, conteudoApi) = ValidarELerArquivos(diretorioSolucao);

    List<AcaoDTO> listaAcoes = ObterAcoesParaGeracao(diretorioSolucao, sql, nomeAssembly, nomeNamespace, esquema, tabela, conteudoXml, conteudoModel, conteudoService, conteudoDto, conteudoApi);

    var sessao = InicializarSessaoChat(configuracao, out var parametrosInferencia);

    //sessao.History.AddMessage(AuthorRole.System, "Sou um especialista em desenvolvimento de software FUNCEF usando Nhibenate mapping (hbm.xml) sempre sigo o modelo expecificado e mantendo o PADRÃO ESTABELECIDO");
    sessao.History.AddMessage(AuthorRole.System, "Você é um assistente técnico que deve apenas interpretar o texto passado, sem executar nem completar códigos. Quando encontrar SQL ou outros comandos, trate apenas como texto literal. Sempre mantenha foco no objetivo definido no contexto. Evite suposições ou complementações desnecessárias.\r\n");
    sessao.History.AddMessage(AuthorRole.Assistant, "Bem-vindo ao assistente de programação! Digite sua pergunta ou 'exit' para sair.");

    string entradaUsuario = "";
    while (entradaUsuario != "exit")
    {
        await ProcessarAcoesUsuario(listaAcoes, sessao, parametrosInferencia);
        entradaUsuario = Console.ReadLine() ?? "";
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Ocorreu um erro: {ex.Message}");
}

static IConfiguration CarregarConfiguracao()
{
    return new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .Build();
}

static (string sql, string nomeAssembly, string nomeNamespace) LerEntradaUsuario()
{
#if !DEBUG
    Console.WriteLine("Digite o script da tabela (pressione Enter em uma linha vazia para finalizar):");

    var construtorEntrada = new System.Text.StringBuilder();
    string? linha = string.Empty;

    while (!string.IsNullOrWhiteSpace(linha = Console.ReadLine()))
    {
        construtorEntrada.AppendLine(linha);
    }

    string sql = construtorEntrada.ToString();

    Console.Write("Digite o nome do assembly: ");
    string nomeAssembly = Console.ReadLine() ?? throw new InvalidOperationException("Assembly name cannot be null.");

    Console.Write("Digite o namespace: ");
    string nomeNamespace = Console.ReadLine() ?? throw new InvalidOperationException("Namespace cannot be null.");

    return (sql, nomeAssembly, nomeNamespace);
#else
    // Para fins de teste, buscar os valores do appsettings.json
    var configuracao = CarregarConfiguracao();
    string sql = configuracao["AppSettings:SqlTeste"] ?? throw new InvalidOperationException("SqlTeste não está configurado no appsettings.json.");
    string nomeAssembly = configuracao["AppSettings:AssemblyName"] ?? "MeuAssembly";
    string nomeNamespace = configuracao["AppSettings:NamespaceName"] ?? "MeuNamespace";
    return (sql, nomeAssembly, nomeNamespace);
#endif
}


static (string conteudoXml, string conteudoModel, string conteudoService, string conteudoDto, string conteudoApi) ValidarELerArquivos(string diretorioSolucao)
{
    var caminhoXml = $"{diretorioSolucao}\\xml\\TipoAtoDecisorio.mapping.hbm.txt";
    var caminhoModel = $"{diretorioSolucao}\\model\\TipoAtoDecisorio.txt";
    var caminhoService = $"{diretorioSolucao}\\service\\TipoAtoDecisorioService.txt";
    var caminhoDto = $"{diretorioSolucao}\\dto\\TipoAtoDecisorioDTO.txt";
    var caminhoApi = $"{diretorioSolucao}\\api\\TipoAtoDecisorioController.txt";

    if (!File.Exists(caminhoXml) || !File.Exists(caminhoModel) || !File.Exists(caminhoService) || !File.Exists(caminhoDto) || !File.Exists(caminhoApi))
    {
        throw new FileNotFoundException("Um ou mais arquivos necessários não foram encontrados.");
    }

    return (
        File.ReadAllText(caminhoXml),
        File.ReadAllText(caminhoModel),
        File.ReadAllText(caminhoService),
        File.ReadAllText(caminhoDto),
        File.ReadAllText(caminhoApi)
    );
}

static ChatSession InicializarSessaoChat(IConfiguration configuracao, out InferenceParams parametrosInferencia)
{
    string? caminhoModeloLLama = configuracao["AppSettings:ModelPathLLama"];
    if (string.IsNullOrEmpty(caminhoModeloLLama))
    {
        throw new InvalidOperationException("O caminho do modelo especificado não pode ser nulo ou vazio.");
    }

    var parametros = new ModelParams(caminhoModeloLLama)
    {
        ContextSize = uint.Parse(configuracao["AppSettings:ContextSize"] ?? throw new InvalidOperationException("AppSettings:ContextSize is not configured.")),
        GpuLayerCount = int.Parse(configuracao["AppSettings:GpuLayerCount"] ?? throw new InvalidOperationException("AppSettings:GpuLayerCount is not configured or is null."))
    };

    var modeloLLama = LLamaWeights.LoadFromFile(parametros);
    var contexto = modeloLLama.CreateContext(parametros);
    var executor = new InteractiveExecutor(contexto);

    var historicoChat = new ChatHistory();

    parametrosInferencia = new InferenceParams()
    {
        //MaxTokens = -1,
        //AntiPrompts = new List<string>(),
        //SamplingPipeline = new DefaultSamplingPipeline()
        //{
        //    //Temperature = 0.3f,
        //    //TopP = 0.8f,
        //    //TopK = 10
        //}
        MaxTokens = 1024, // ou outro limite que suporte sua resposta
        AntiPrompts = new List<string> { "User:" }, // pode incluir "User:" se estiver usando chat com turnos
        SamplingPipeline = new DefaultSamplingPipeline()
        {
            Temperature = 0.5f, // Reduz a aleatoriedade. Foco em precisão.
            TopP = 0.6f,        // Reduz a diversidade, evitando variações desnecessárias.
            TopK = 30,          // Restringe ainda mais as opções. Melhora a coerência.
        }
    };

    return new ChatSession(executor, historicoChat);
}

static async Task ProcessarAcoesUsuario(List<AcaoDTO> listaAcoes, ChatSession sessao, InferenceParams parametrosInferencia)
{

    foreach (var item in listaAcoes)
    {
        Console.WriteLine("Iniciar tarefa:" + item.MensagemCompleta);

        string respostaCompleta = string.Empty;

        await foreach (var texto in sessao.ChatAsync(
            new ChatHistory.Message(AuthorRole.User, item.MensagemSimples),
            parametrosInferencia))
        {
            respostaCompleta += texto;
            Console.Write(texto);
        }

        respostaCompleta = item.Nome == "CriarXML" ? ExtrairMapeamentoHibernate(respostaCompleta) : respostaCompleta;
        CriarArquivo(item.Caminho, item.MensagemCompleta, item.Nome);
    }
}

static string CriarXML(string sql, string esquema, string tabela, string nomeAssembly, string nomeNamespace, string conteudoXml)
{
    var c = ExtrairColunasComDetalhes(sql);
    var colunas = string.Join(", ", c);

    return @$"
Função: GerarXML
Objetivo: Gerar um texto no formato de XML de mapeamento NHibernate (.hbm.xml) válido, conforme o padrão corporativo, a partir de uma definição do CONTEXTO PARA A GERAÇÃO.

# REGRAS OBRIGATÓRIAS (VALIDAÇÃO):
    - Trate todo o conteúdo entre aspas como texto literal, sem interpretar como código executável
:
    - Nunca execute ou interprete o SQL fornecido, apenas gere o XML de mapeamento a partir dele.
 
    - Nunca invente colunas, campos, propriedades ou estruturas que não constem literalmente no sql.

    - Nunca modifique nomes de colunas ou propriedades.

    - Nunca adicione atributos, tags ou geradores não especificados.

    - O XML gerado deve ser 100% compatível com o NHibernate, sem erros sintáticos.

    - A estrutura, tags e atributos devem seguir exatamente o padrão abaixo.

    - EXTRAIR DO SQL O NOME DAS COLUNAS E SEUS TIPOS. (CONTEXTO PARA A GERAÇÃO)

    - PADRÃO XML CORPORATIVO:
        ## Declaração XML: ""<?xml version=""1.0"" encoding=""utf-8"" ?>""
        ## Tag <hibernate-mapping> com atributos: ""<hibernate-mapping xmlns=""urn:nhibernate-mapping-2.2"" assembly=""{{nomeAssembly}}"" namespace=""{{nomeNamespace}}"" default-lazy=""true"" default-cascade=""none"">""
        ## Tag <typedef> obrigatória: ""<typedef class=""FuncefEngine.NHibernateHelpers.ZeroOneType, FuncefEngine"" name=""ZeroOneType""/>""
        ## Tag <class>
            - name: Nome da classe em PascalCase
            - table: Nome da tabela (ex: ESQ_TABELA)
            = schema: Nome do esquema (ex: ESQ_ESQUEMA)
    - Tag <id> com <generator>:
        ""<id name=""Id"" column=""{{nomeColunaPK}}"">
            <generator class=""sequence"">
                <param name=""sequence"">SQ_{{nomeTabela}}</param>
            </generator>
        </id>""

    - Tag <property> para cada coluna adicional:
       ## name: Nome da propriedade em PascalCase
       ## column: Nome real da coluna no banco

    - CONTEXTO PARA A GERAÇÃO:
        Colunas
        ""{colunas}""

    - Configurações:
        ## AssemblyName: {nomeAssembly}
        ## Namespace: {nomeNamespace}
        ## Table Schema: {esquema}
        ## Table Name: {tabela}

# SAÍDA ESPERADA:        
     - Obedeça as REGRAS OBRIGATÓRIAS 
     - Gere o texto do xml mapeamento NHibernate (.hbm.xml) 
     - Analise passo a passo o que foi gerado verificando se está no padrão especificado nas REGRAS OBRIGATÓRIAS.

";
}

static string CriarModel(string exemploModel)
{
    return @$"
1. De acordo com o XML gerado anteriormente crie a model de acordo com o exemplo:
  - Exemplo Model = {exemploModel}
2. Responda somente com o código final (ou resultado final), sem explicações, comentários ou passos detalhados.";
}

static string CriarService(string exemploService)
{
    return @$"
1. De acordo com a model gerado anteriormente crie a service de acordo com o exemplo:
  - Exemplo Service = {exemploService}
2. Responda somente com o código final (ou resultado final), sem explicações, comentários ou passos detalhados.";
}

static string CriarDTO(string exemploDto)
{
    return @$"
1. De acordo com a model gerado anteriormente crie a DTO de acordo com o exemplo:
  - Exemplo DTO = {exemploDto}
2. Responda somente com o código final (ou resultado final), sem explicações, comentários ou passos detalhados.";
}

static string CriarAPI(string exemploApi)
{
    return @$"
1. Crie a API de acordo com o exemplo:
  - Exemplo API = {exemploApi}
2. Responda somente com o código final (ou resultado final), sem explicações, comentários ou passos detalhados.";
}



static string ExtrairMapeamentoHibernate(string respostaCompleta)
{
    string conteudoInterno = respostaCompleta;
    var regex = new Regex(@"<hibernate-mapping\b[^>]*>(.*?)<\/hibernate-mapping>", RegexOptions.Singleline);
    var matchXML = regex.Match(respostaCompleta);
    if (matchXML.Success)
    {
        conteudoInterno = matchXML.Groups[1].Value;
    }
    return conteudoInterno;
}

static void CriarArquivo(string pasta, string conteudo, string nomeArquivo)
{
    string caminhoArquivo = $"{pasta}\\{nomeArquivo}";
    File.WriteAllText(caminhoArquivo, conteudo);
}

try
{
    var configuration = LoadConfiguration();
    string baseDir = AppContext.BaseDirectory;
    string solutionDir = Path.GetFullPath(Path.Combine(baseDir, configuration["AppSettings:SolutionDirectory"] ?? throw new InvalidOperationException("SolutionDirectory is not configured.")));

    var (sql, assemblyName, namespaceName) = ReadUserInput();

    var match = Regex.Match(sql, @"(?i)CREATE\s+TABLE\s+(?:""?(\w+)""?\.)?""?(\w+)""?");
    if (!match.Success || match.Groups.Count < 3)
    {
        Console.WriteLine("Não foi possível encontrar o nome da tabela no script fornecido.");
        return;
    }
    string esquema = match.Groups[1].Value;
    string tabela = match.Groups[2].Value;

    var (xmlContent, modelContent, serviceContent, dtoContent, apiContent) = ValidateAndReadFiles(solutionDir);

    var listaAcoes = new List<AcaoDTO>
    {
        new("CriarXML", $"{solutionDir}\\xml\\", tabela, CriarXML(sql, esquema, tabela, assemblyName, namespaceName, xmlContent), $"Execute a Função GerarXML"),
        new("CriarModel", $"{solutionDir}\\model\\", tabela, CriarModel(modelContent), "Gere a model relacionado ao xml gerado"),
        new("CriarService", $"{solutionDir}\\service\\", tabela, CriarService(serviceContent), "Gere a service"),
        new("CriarDTO", $"{solutionDir}\\dto\\", tabela, CriarDTO(dtoContent), "Gere a DTO"),
        new("CriarAPI", $"{solutionDir}\\api\\", tabela, CriarAPI(apiContent), "Gere a API")
    };

    var session = InitializeChatSession(configuration, out var inferenceParams);

    Console.WriteLine("Bem-vindo ao assistente de programação! Digite sua pergunta ou 'exit' para sair.");
    string userInput = Console.ReadLine() ?? "";

    while (userInput != "exit")
    {
        await ProcessUserActions(listaAcoes, session, inferenceParams);
        userInput = Console.ReadLine() ?? "";
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Ocorreu um erro: {ex.Message}");
}

static IConfiguration LoadConfiguration()
{
    return new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .Build();
}

static (string sql, string assemblyName, string namespaceName) ReadUserInput()
{
    Console.WriteLine("Digite o script da tabela (pressione Enter em uma linha vazia para finalizar):");

    var inputBuilder = new System.Text.StringBuilder();
    string? line = string.Empty;

    while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
    {
        inputBuilder.AppendLine(line);
    }

    string sql = inputBuilder.ToString();

    Console.Write("Digite o nome do assembly: ");
    string assemblyName = Console.ReadLine() ?? throw new InvalidOperationException("Assembly name cannot be null.");

    Console.Write("Digite o namespace: ");
    string namespaceName = Console.ReadLine() ?? throw new InvalidOperationException("Namespace cannot be null.");

    return (sql, assemblyName, namespaceName);
}

static (string xmlContent, string modelContent, string serviceContent, string dtoContent, string apiContent) ValidateAndReadFiles(string solutionDir)
{
    var xmlPath = $"{solutionDir}\\xml\\TipoAtoDecisorio.mapping.hbm.txt";
    var modelPath = $"{solutionDir}\\model\\TipoAtoDecisorio.txt";
    var servicePath = $"{solutionDir}\\service\\TipoAtoDecisorioService.txt";
    var dtoPath = $"{solutionDir}\\dto\\TipoAtoDecisorioDTO.txt";
    var apiPath = $"{solutionDir}\\api\\TipoAtoDecisorioController.txt";

    if (!File.Exists(xmlPath) || !File.Exists(modelPath) || !File.Exists(servicePath) || !File.Exists(dtoPath) || !File.Exists(apiPath))
    {
        throw new FileNotFoundException("Um ou mais arquivos necessários não foram encontrados.");
    }

    return (
        File.ReadAllText(xmlPath),
        File.ReadAllText(modelPath),
        File.ReadAllText(servicePath),
        File.ReadAllText(dtoPath),
        File.ReadAllText(apiPath)
    );
}

static ChatSession InitializeChatSession(IConfiguration configuration, out InferenceParams inferenceParams)
{
    string? modelPathLLama = configuration["AppSettings:ModelPathLLama"];
    if (string.IsNullOrEmpty(modelPathLLama))
    {
        throw new InvalidOperationException("O caminho do modelo especificado não pode ser nulo ou vazio.");
    }

    var parameters = new ModelParams(modelPathLLama)
    {
        ContextSize = uint.Parse(configuration["AppSettings:ContextSize"] ?? throw new InvalidOperationException("AppSettings:ContextSize is not configured.")),
        GpuLayerCount = int.Parse(configuration["AppSettings:GpuLayerCount"] ?? throw new InvalidOperationException("AppSettings:GpuLayerCount is not configured or is null."))

    };

    var modelLLama = LLamaWeights.LoadFromFile(parameters);
    var context = modelLLama.CreateContext(parameters);
    var executor = new InteractiveExecutor(context);

    var chatHistory = new ChatHistory();
    chatHistory.AddMessage(AuthorRole.System, "Sou um especialista em programação que gera código crud conforme os padrões fornecidos, respondendo de forma precisa e objetiva.");
    chatHistory.AddMessage(AuthorRole.Assistant, "Olá! Como posso ajudá-lo hoje?");

    inferenceParams = new InferenceParams()
    {
        MaxTokens = -1,
        AntiPrompts = new List<string> { "Usuário:" },
        SamplingPipeline = new DefaultSamplingPipeline()
        {
            Temperature = 0.3f,
            TopP = 0.8f,
            TopK = 10
        }
    };

    return new ChatSession(executor, chatHistory);
}

static async Task ProcessUserActions(List<AcaoDTO> listaAcoes, ChatSession session, InferenceParams inferenceParams)
{
    foreach (var item in listaAcoes)
    {
        Console.WriteLine("Iniciar tarefa:" + item.MensagemCompleta);
        string fullResponse = string.Empty;

        await foreach (var text in session.ChatAsync(
            new ChatHistory.Message(AuthorRole.User, item.MensagemSimples),
            inferenceParams))
        {
            fullResponse += text;
            Console.Write(text);
        }

        fullResponse = item.Nome == "CriarXML" ? ExtrairHibernateMapping(fullResponse) : fullResponse;
        CriarArquivo(item.Caminho, item.MensagemCompleta, item.Nome);
    }
}

static void ShowLoading(string message, CancellationToken cancellationToken)
{
    string[] loadingSymbols = { ".", " .", "  .", " " };
    int counter = 0;

    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Verifica se o console está disponível antes de escrever
            if (!Console.IsOutputRedirected)
            {
                Console.Write($"\r{message} {loadingSymbols[counter++ % loadingSymbols.Length]}");
            }

            // Usa Task.Delay para respeitar o CancellationToken
            Task.Delay(500, cancellationToken).Wait(cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
        // O cancelamento é esperado, então não faz nada aqui
    }
    finally
    {
        // Garante que a linha será limpa ao sair, mas apenas se o carregamento não foi interrompido
        if (!Console.IsOutputRedirected && !cancellationToken.IsCancellationRequested)
        {
            Console.Write("\r" + new string(' ', Console.WindowWidth) + "\r");
        }
    }
}

static string ExtrairHibernateMapping(string fullResponse)
{
    string innerContent = fullResponse;
    var regex = new Regex(@"<hibernate-mapping\b[^>]*>(.*?)<\/hibernate-mapping>", RegexOptions.Singleline);
    var matchXML = regex.Match(fullResponse);
    if (matchXML.Success)
    {
        innerContent = matchXML.Groups[1].Value;
    }
    return innerContent;
}

static List<AcaoDTO> ObterAcoesParaGeracao(string diretorioSolucao, string sql, string nomeAssembly, string nomeNamespace, string esquema, string tabela, string conteudoXml, string conteudoModel, string conteudoService, string conteudoDto, string conteudoApi)
{
    return new List<AcaoDTO>
    {
        new("CriarXML", $"{diretorioSolucao}\\xml\\", tabela, CriarXML(sql, esquema, tabela, nomeAssembly, nomeNamespace, conteudoXml), $"Crie o xml da tabela {esquema}.{tabela}"),
        new("CriarModel", $"{diretorioSolucao}\\model\\", tabela, CriarModel(conteudoModel), "Gere a model relacionado ao xml gerado"),
        new("CriarService", $"{diretorioSolucao}\\service\\", tabela, CriarService(conteudoService), "Gere a service"),
        new("CriarDTO", $"{diretorioSolucao}\\dto\\", tabela, CriarDTO(conteudoDto), "Gere a DTO"),
        new("CriarAPI", $"{diretorioSolucao}\\api\\", tabela, CriarAPI(conteudoApi), "Gere a API")
    };
}

static (string esquema, string tabela) ObterEsquemaETabela(string sql)
{
    var match = Regex.Match(sql, @"(?i)CREATE\s+TABLE\s+(?:""?(\w+)""?\.)?""?(\w+)""?");
    if (!match.Success || match.Groups.Count < 3)
    {
        Console.WriteLine("Não foi possível encontrar o nome da tabela no script fornecido.");
    }
    var esquema = match.Groups[1].Value;
    var tabela = match.Groups[2].Value;
    return (esquema, tabela);
}

static List<string> ExtrairColunasComDetalhes(string sql)
{
    // Pega só o conteúdo entre parênteses
    var inicio = sql.IndexOf('(');
    var fim = sql.LastIndexOf(')');
    if (inicio < 0 || fim < 0 || fim <= inicio) return [];

    var colunasDefinicoes = sql.Substring(inicio + 1, fim - inicio - 1);

    var regex = new Regex(
        @"(?i)\b(\w+)\s+([A-Z0-9_]+(?:\s*\(\s*\d+(?:\s*,\s*\d+)?\s*\))?)\s*((?:NOT\s+NULL|NULL|DEFAULT\s+[^,\n\r)]+|UNIQUE|PRIMARY\s+KEY|CHECK\s*\([^)]+\)|REFERENCES\s+\w+\s*\([^)]+\))*)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline
    );

    var matches = regex.Matches(colunasDefinicoes);
    var colunas = new List<string>();

    foreach (Match match in matches)
    {
        var nome = match.Groups[1].Value.Trim();
        var tipo = match.Groups[2].Value.Trim();
        var extras = match.Groups[3].Value.Trim().Replace("\n", " ").Replace("\r", " ");

        colunas.Add($"{nome}|{tipo}|{extras}");
    }

    return colunas;
}
