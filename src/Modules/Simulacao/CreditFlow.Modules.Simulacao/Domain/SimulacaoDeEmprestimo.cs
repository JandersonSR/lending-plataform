using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Simulacao.Domain.Calculo;

namespace CreditFlow.Modules.Simulacao.Domain;

/// <summary>
/// Agregado: uma simulação calculada. É IMUTÁVEL depois de criada: simular de novo é outra simulação.
/// Invariantes: valor e prazo dentro da faixa; a tabela, o IOF e o CET são SEMPRE os do motor para
/// esses parâmetros (não há como montar uma simulação com tabela "editada").
/// O nome não é "Simulacao" porque colidiria com o namespace CreditFlow.Modules.Simulacao.
/// </summary>
public sealed class SimulacaoDeEmprestimo
{
    /// <summary>Por quanto tempo a simulação vale para virar proposta (as taxas mudam).</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromDays(7);

    private SimulacaoDeEmprestimo(
        SimulacaoId id, Guid? clienteId, ValorDoEmprestimo valor, PrazoEmMeses prazo, SistemaDeAmortizacao sistema,
        FaixaDeScore faixa, DateOnly dataContratacao, DateTimeOffset criadaEm, ResultadoDoCalculo resultado)
    {
        Id = id;
        ClienteId = clienteId;
        Valor = valor;
        Prazo = prazo;
        Sistema = sistema;
        Faixa = faixa;
        DataContratacao = dataContratacao;
        CriadaEm = criadaEm;
        Resultado = resultado;
    }

    public SimulacaoId Id { get; }
    public Guid? ClienteId { get; }
    public ValorDoEmprestimo Valor { get; }
    public PrazoEmMeses Prazo { get; }
    public SistemaDeAmortizacao Sistema { get; }
    public FaixaDeScore Faixa { get; }
    public DateOnly DataContratacao { get; }
    public DateTimeOffset CriadaEm { get; }
    public DateTimeOffset ValidaAte => CriadaEm + Validade;
    public ResultadoDoCalculo Resultado { get; }

    public bool Vencida(DateTimeOffset agora) => agora > ValidaAte;

    /// <summary>
    /// Cria e calcula. Os dias do IOF contam a partir da data de contratação em Brasília
    /// (quem chama passa a data; o domínio não lê relógio).
    /// </summary>
    public static Result<SimulacaoDeEmprestimo> Criar(
        decimal valor, int prazoMeses, SistemaDeAmortizacao sistema, FaixaDeScore faixa, Guid? clienteId,
        DateOnly dataContratacao, DateTimeOffset agora, RegrasDeIof? iof = null)
    {
        ArgumentNullException.ThrowIfNull(faixa);

        var v = ValorDoEmprestimo.Criar(valor);
        var p = PrazoEmMeses.Criar(prazoMeses);
        if (v.Falhou || p.Falhou)
            return Erro.JuntarValidacoes(new[] { v.Erro, p.Erro }.OfType<Erro>());
        if (!Enum.IsDefined(sistema))
            return SimulacaoErros.SistemaInvalido;

        var resultado = MotorDeCalculo.Calcular(new ParametrosDoCalculo(
            v.Valor.Valor, p.Valor.Meses, faixa.TaxaMensal, sistema, dataContratacao, iof ?? RegrasDeIof.PessoaFisica));

        return new SimulacaoDeEmprestimo(
            SimulacaoId.Novo(), clienteId, v.Valor, p.Valor, sistema, faixa, dataContratacao, agora, resultado);
    }

    /// <summary>
    /// Remonta uma simulação gravada (repositório Mongo). Não recalcula: o que foi mostrado ao cliente
    /// é o que vale, mesmo que o motor mude depois. Por isso a tabela inteira é guardada.
    /// </summary>
    public static SimulacaoDeEmprestimo Reconstituir(
        SimulacaoId id, Guid? clienteId, decimal valor, int prazoMeses, SistemaDeAmortizacao sistema,
        FaixaDeScore faixa, DateOnly dataContratacao, DateTimeOffset criadaEm, ResultadoDoCalculo resultado)
    {
        var v = ValorDoEmprestimo.Criar(valor);
        var p = PrazoEmMeses.Criar(prazoMeses);
        if (v.Falhou || p.Falhou)
            throw new InvalidOperationException($"Simulação {id} gravada com dados inválidos.");
        return new SimulacaoDeEmprestimo(id, clienteId, v.Valor, p.Valor, sistema, faixa, dataContratacao, criadaEm, resultado);
    }
}
