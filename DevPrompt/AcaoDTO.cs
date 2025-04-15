public class AcaoDTO
{
    public string Nome { get; set; }
    public string Caminho { get; set; }
    public string MensagemCompleta { get; set; }
    public string MensagemSimples { get; set; }
    public string Tabela { get; set; }
    public AcaoDTO(string nome,  string caminho, string tabela, string mensagemCompleta, string mensagemSimples)
    {
        Nome = nome;
        Caminho = caminho;
        MensagemCompleta = mensagemCompleta;
        Tabela = tabela;
        MensagemSimples = mensagemSimples;
    }

}