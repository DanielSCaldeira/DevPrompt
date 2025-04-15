using LLama;
using LLama.Common;
using LLama.Sampling;
using System.Text.RegularExpressions;
using static LLama.Common.ChatHistory;
using static System.Net.Mime.MediaTypeNames;

try
{

    string baseDir = AppContext.BaseDirectory;
    string solutionDir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\.."));

    Console.WriteLine("Digite o script da tabela (pressione Enter em uma linha vazia para finalizar):");

    var inputBuilder = new System.Text.StringBuilder();
    string? line;

    while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
    {
        inputBuilder.AppendLine(line);
    }

    string sql = inputBuilder.ToString();

    var match = Regex.Match(sql, @"(?i)CREATE\s+TABLE\s+(?:""?(\w+)""?\.)?""?(\w+)""?");
    if (!match.Success)
    {
        Console.WriteLine("Não foi possível encontrar o nome da tabela no script fornecido.");
    }
    string esquema = match.Groups[1].Value;
    string tabela = match.Groups[2].Value;


    Console.Write("Digite o nome do assembly: ");
    string assemblyName = Console.ReadLine();

    Console.Write("Digite o namespace: ");
    string namespaceName = Console.ReadLine();

    // Caminhos dos arquivos
    var xmlPath = $"{solutionDir}\\xml\\TipoAtoDecisorio.mapping.hbm.txt";
    var modelPath = $"{solutionDir}\\model\\TipoAtoDecisorio.txt";
    var servicePath = $"{solutionDir}\\service\\TipoAtoDecisorioService.txt";
    var dtoPath = $"{solutionDir}\\dto\\TipoAtoDecisorioDTO.txt";
    var apiPath = $"{solutionDir}\\api\\TipoAtoDecisorioController.txt";

    // Lendo o conteúdo dos arquivos
    var xmlContent = File.Exists(xmlPath) ? File.ReadAllText(xmlPath) : string.Empty;
    var modelContent = File.Exists(modelPath) ? File.ReadAllText(modelPath) : string.Empty;
    var serviceContent = File.Exists(servicePath) ? File.ReadAllText(servicePath) : string.Empty;
    var dtoContent = File.Exists(dtoPath) ? File.ReadAllText(dtoPath) : string.Empty;
    var apiContent = File.Exists(apiPath) ? File.ReadAllText(apiPath) : string.Empty;
    var listaAcoes = new List<AcaoDTO>
    {
        new AcaoDTO("CriarXML", $"{solutionDir}\\xml\\", tabela, CriarXML(sql, esquema, tabela, assemblyName, namespaceName, xmlContent), $"Gere o xml da tabela {tabela}.{esquema}"),
        new AcaoDTO("CriarModel", $"{solutionDir}\\model\\", tabela, CriarModel(modelContent), "Gere"),
        new AcaoDTO("CriarService", $"{solutionDir}\\service\\", tabela, CriarService(serviceContent), "Gere"),
        new AcaoDTO("CriarDTO", $"{solutionDir}\\dto\\", tabela, CriarDTO(dtoContent), ""),
        new AcaoDTO("CriarAPI", $"{solutionDir}\\api\\", tabela, CriarAPI(apiContent), "")
    };


    Console.WriteLine("Bem-vindo ao assistente de programação! Digite sua pergunta ou 'exit' para sair.");

    string modelPathLLama = @"C:\Modelos\mistral-7b-instruct-v0.1.Q4_K_M.gguf"; // Altere para o caminho do seu modelo.
    if (!File.Exists(modelPathLLama))
    {
        Console.WriteLine("O caminho do modelo especificado não existe.");
        return;
    }

    var parameters = new ModelParams(modelPathLLama)
    {
        ContextSize = 1024, // Tamanho máximo do contexto.
        GpuLayerCount = 5   // Ajuste conforme a memória da sua GPU.
    };

    using var modelLLama = LLamaWeights.LoadFromFile(parameters);
    using var context = modelLLama.CreateContext(parameters);
    var executor = new InteractiveExecutor(context);

    // Adicionando histórico de chat para orientar o comportamento da IA.
    var chatHistory = new ChatHistory();
    chatHistory.AddMessage(AuthorRole.System, "Sou um especialista em programação que gera código crud conforme os padrões fornecidos, respondendo de forma precisa e objetiva.");
    chatHistory.AddMessage(AuthorRole.Assistant, "Olá! Como posso ajudá-lo hoje?");

    ChatSession session = new(executor, chatHistory);

    var inferenceParams = new InferenceParams()
    {
        MaxTokens = -1, // Limite de tokens na resposta.
        AntiPrompts = new List<string> { "Usuário:" }, // Interrompe a geração ao encontrar antiprompts.
        SamplingPipeline = new DefaultSamplingPipeline()
        {
            Temperature = 0.3f, // Respostas mais determinísticas.
            TopP = 0.8f,        // Restringe a probabilidade cumulativa.
            TopK = 10
        }
    };

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("A sessão de chat foi iniciada. Aperte ENTER para iniciar");
    Console.ForegroundColor = ConsoleColor.Green;
    string userInput = Console.ReadLine() ?? "";

    while (userInput != "exit")
    {
        using var cts = new CancellationTokenSource();
        var loadingTask = Task.Run(() => ShowLoading("Processando", cts.Token));

        try
        {

            for (int i = 0; i < listaAcoes.Count; i++)
            {
                var item = listaAcoes[i];
                chatHistory.AddMessage(AuthorRole.User, item.MensagemCompleta);
                chatHistory.AddMessage(AuthorRole.Assistant, $"Tarefa guardada {item.Nome}");
                Console.WriteLine("Iniciar tarefa:" + item.MensagemCompleta);
                string fullResponse = string.Empty;
                await foreach (
                var text in session.ChatAsync(
                    new ChatHistory.Message(AuthorRole.User, item.MensagemSimples),
                    inferenceParams))
                {
                    fullResponse += text;
                    Console.Write(text);
                    cts.Cancel();
                    await loadingTask;
                }

              
                Console.ForegroundColor = ConsoleColor.White;
                chatHistory.AddMessage(AuthorRole.Assistant, $"Tarefa Finalizada {item.Nome}");

                fullResponse = i == 0 ? ExtrairHibernateMapping(fullResponse, item.Caminho) : fullResponse;
                CriarArquivo(item.Caminho, item.MensagemCompleta, item.Nome);
            }
        }
        catch (Exception ex)
        {
            Console.Write(ex.Message);
        }


        Console.ForegroundColor = ConsoleColor.Green;
        userInput = Console.ReadLine() ?? "";
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Ocorreu um erro: {ex.Message}");
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

static string CriarXML(string sql, string esquema, string tabela, string assemblyName, string namespaceName, string xmlContent)
{
    return @$"
1. Substitua, crie e refatore de acordo com as colunas da tabela informada.
2. Use os valores: 
   - Exemplo XML = {xmlContent}
   - Tabela = {sql.Replace("\n","")}
   - AssemblyName = {assemblyName}
   - NamespaceName = {namespaceName}
   - Esquema da Tabela = {esquema}
   - Nome da Tabela = {tabela}
3. Crie o arquivo mapping XML como no exemplo acima.
4. Responda somente com o código final (ou resultado final), sem explicações, comentários ou passos detalhados.";
}

static string CriarModel(string modelExemplo)
{
    return @$"
1. De acordo com o XML gerado anteriormente crie a model de acordo com o exemplo:
  - Exemplo Model = {modelExemplo}
2. Responda somente com o código final (ou resultado final), sem explicações, comentários ou passos detalhados.";
}

static string CriarService(string serviceExemplo)
{
    return @$"
1. De acordo com a model gerado anteriormente crie a service de acordo com o exemplo:
  - Exemplo Service = {serviceExemplo}
2. Responda somente com o código final (ou resultado final), sem explicações, comentários ou passos detalhados.";
}

static string CriarDTO(string dtoExemplo)
{
    return @$"
1. De acordo com a model gerado anteriormente crie a DTO de acordo com o exemplo:
  - Exemplo DTO = {dtoExemplo}
2. Responda somente com o código final (ou resultado final), sem explicações, comentários ou passos detalhados.";
}

static string CriarAPI(string apiExemplo)
{
    return @$"
1. Crie a API de acordo com o exemplo:
  - Exemplo API = {apiExemplo}
2. Responda somente com o código final (ou resultado final), sem explicações, comentários ou passos detalhados.";
}

static string ExtrairHibernateMapping(string fullResponse, string xmlPath)
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

static void CriarArquivo(string pasta, string conteudo, string nomeArquivo)
{
    string caminhoArquivo = $"{pasta}\\{nomeArquivo}";
    // Cria o arquivo e escreve o conteúdo
    File.WriteAllText(caminhoArquivo, conteudo);
}