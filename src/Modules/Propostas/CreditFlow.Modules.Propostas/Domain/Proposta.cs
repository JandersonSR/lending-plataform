using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.ValueObjects;

namespace CreditFlow.Modules.Propostas.Domain;

/// <summary>
/// Agregado Proposta: o pedido de crédito de um cliente a partir de uma simulação.
/// Toda mudança de estado passa por um método daqui, que confere a transição e registra o evento.
/// Ninguém de fora escreve em <see cref="Status"/>.
/// </summary>
public sealed class Proposta
{
    private readonly List<IEventoDeDominio> _eventos = [];
    private readonly List<ParcelaPrevista> _parcelas = [];

    private Proposta() { } // EF Core

    public Guid Id { get; private set; }
    public Guid ClienteId { get; private set; }
    /// <summary>Claim <c>sub</c> de quem pediu. É por ela que se decide "a proposta é sua?".</summary>
    public string SolicitanteId { get; private set; } = "";
    public Guid SimulacaoId { get; private set; }

    // Condições copiadas da simulação no momento do pedido (snapshot): se a tabela de taxas mudar
    // amanhã, esta proposta continua com as condições que o cliente viu.
    public Dinheiro ValorSolicitado { get; private set; }
    /// <summary>O que cai na conta do cliente: o solicitado menos o IOF. É o valor do desembolso.</summary>
    public Dinheiro ValorLiberado { get; private set; }
    public int PrazoMeses { get; private set; }
    public Percentual TaxaMensal { get; private set; }
    public Percentual CetAnual { get; private set; }
    public string Sistema { get; private set; } = "";
    public IReadOnlyList<ParcelaPrevista> Parcelas => _parcelas;

    // O que a análise viu (auditoria: a decisão precisa ser explicável depois).
    public int ScoreNaAnalise { get; private set; }
    public Dinheiro RendaNaAnalise { get; private set; }
    public decimal Comprometimento { get; private set; }

    public StatusProposta Status { get; private set; }
    public bool DecisaoAutomatica { get; private set; }
    public string? CodigoMotivo { get; private set; }
    public string? Justificativa { get; private set; }
    public string? AnalisadoPor { get; private set; }
    public string? ChavePix { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? DecididaEm { get; private set; }
    public DateTimeOffset? ValidaAte { get; private set; }
    public DateTimeOffset? ContratadaEm { get; private set; }
    public DateTimeOffset? FinalizadaEm { get; private set; }

    /// <summary>
    /// Concorrência otimista. No PostgreSQL é a coluna de sistema <c>xmin</c> (id da transação que gravou a
    /// linha por último): não existe no DDL, não ocupa espaço, e muda sozinha a cada UPDATE.
    /// </summary>
    public uint Versao { get; private set; }

    public IReadOnlyList<IEventoDeDominio> EventosDeDominio => _eventos;
    public void LimparEventos() => _eventos.Clear();

    public Dinheiro MaiorParcela => _parcelas.Count == 0
        ? Dinheiro.Zero
        : Dinheiro.DeCentavos(_parcelas.Max(p => p.ValorCentavos));

    /// <summary>Cria a proposta e já roda a análise automática: ela nasce aprovada, recusada ou na fila do analista.</summary>
    public static Result<Proposta> Criar(
        Guid id, Guid clienteId, string solicitanteId, CondicoesDaSimulacao condicoes,
        int score, Dinheiro rendaMensal, bool temConsentimento,
        ParametrosDeAnalise parametros, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(condicoes);
        ArgumentNullException.ThrowIfNull(parametros);
        ArgumentException.ThrowIfNullOrWhiteSpace(solicitanteId);

        if (!temConsentimento)
            return PropostasErros.SemConsentimento;
        if (agora - condicoes.RealizadaEm > TimeSpan.FromDays(parametros.ValidadeDaSimulacaoDias))
            return PropostasErros.SimulacaoVencida;
        if (condicoes.ClienteId is { } dono && dono != clienteId)
            return PropostasErros.SimulacaoDeOutroCliente;
        if (condicoes.Parcelas.Count != condicoes.PrazoMeses || condicoes.Parcelas.Any(p => p.ValorCentavos <= 0))
            throw new ArgumentException("Simulação inconsistente: o número de parcelas tem de ser igual ao prazo.", nameof(condicoes));

        var proposta = new Proposta
        {
            Id = id,
            ClienteId = clienteId,
            SolicitanteId = solicitanteId,
            SimulacaoId = condicoes.SimulacaoId,
            ValorSolicitado = condicoes.ValorSolicitado,
            ValorLiberado = condicoes.ValorLiberado,
            PrazoMeses = condicoes.PrazoMeses,
            TaxaMensal = condicoes.TaxaMensal,
            CetAnual = condicoes.CetAnual,
            Sistema = condicoes.Sistema,
            ScoreNaAnalise = score,
            RendaNaAnalise = rendaMensal,
            CriadaEm = agora,
        };
        proposta._parcelas.AddRange(condicoes.Parcelas.OrderBy(p => p.Numero));
        proposta._eventos.Add(new PropostaCriada(id, clienteId, condicoes.SimulacaoId,
            condicoes.ValorSolicitado.Centavos, condicoes.PrazoMeses, agora));

        var analise = AnaliseDeCredito.Avaliar(score, rendaMensal, proposta.MaiorParcela, condicoes.ValorSolicitado, parametros);
        proposta.Comprometimento = decimal.Round(Math.Min(analise.Comprometimento, 99m), 4);
        proposta.CodigoMotivo = analise.Codigo;
        proposta.Justificativa = analise.Explicacao;

        switch (analise.Decisao)
        {
            case Decisao.Aprovar:
                proposta.Aprovar(automatica: true, analista: null, agora, parametros.ValidadeDaAprovacaoDias);
                break;
            case Decisao.Recusar:
                proposta.Recusar(automatica: true, analista: null, analise.Codigo, agora);
                break;
            default:
                proposta.Status = StatusProposta.EmAnalise;
                proposta._eventos.Add(new PropostaEnviadaParaAnalise(id, clienteId, analise.Codigo, agora));
                break;
        }
        return proposta;
    }

    /// <summary>Decisão do analista.</summary>
    public Result AprovarManualmente(string analista, string? justificativa, DateTimeOffset agora, int validadeDias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analista);
        if (Status != StatusProposta.EmAnalise)
            return PropostasErros.TransicaoInvalida(Status, "aprovar");
        if (string.IsNullOrWhiteSpace(justificativa) || justificativa.Trim().Length < 10)
            return PropostasErros.MotivoObrigatorio;

        Justificativa = justificativa.Trim();
        CodigoMotivo = "aprovacao-manual";
        Aprovar(automatica: false, analista, agora, validadeDias);
        return Result.Ok();
    }

    public Result RecusarManualmente(string analista, string? motivo, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analista);
        if (Status != StatusProposta.EmAnalise)
            return PropostasErros.TransicaoInvalida(Status, "recusar");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
            return PropostasErros.MotivoObrigatorio;

        Justificativa = motivo.Trim();
        Recusar(automatica: false, analista, "recusa-manual", agora);
        return Result.Ok();
    }

    /// <summary>
    /// O cliente assina. Gera o cronograma com vencimentos a partir da data da contratação e o evento
    /// que inicia a saga de desembolso.
    /// </summary>
    public Result Contratar(string? chavePix, DateTimeOffset agora)
    {
        if (Status != StatusProposta.Aprovada)
            return PropostasErros.TransicaoInvalida(Status, "contratar");
        if (ValidaAte is { } limite && agora > limite)
        {
            Expirar(agora);
            return PropostasErros.AprovacaoVencida;
        }
        if (string.IsNullOrWhiteSpace(chavePix) || chavePix.Trim().Length > 77)
            return PropostasErros.ChavePixObrigatoria;

        ChavePix = chavePix.Trim();
        Status = StatusProposta.Contratada;
        ContratadaEm = agora;

        // Sempre a partir da data da contratação (e não "da parcela anterior"): contratou em 31/01, vence
        // 28/02, 31/03, 30/04... Somar mês a mês a partir de 28/02 escorregaria todas para o dia 28.
        var contratacao = DateOnly.FromDateTime(agora.UtcDateTime);
        var cronograma = _parcelas
            .Select(p => new ParcelaContratada(p.Numero, p.ValorCentavos, contratacao.AddMonths(p.Numero)))
            .ToList();
        _eventos.Add(new ContratoAssinado(Id, ClienteId, SolicitanteId, ValorSolicitado.Centavos, ValorLiberado.Centavos,
            ChavePix, cronograma, agora));
        return Result.Ok();
    }

    public Result Cancelar(DateTimeOffset agora)
    {
        if (Status is not (StatusProposta.EmAnalise or StatusProposta.Aprovada))
            return PropostasErros.TransicaoInvalida(Status, "cancelar");
        Status = StatusProposta.Cancelada;
        FinalizadaEm = agora;
        _eventos.Add(new PropostaCancelada(Id, ClienteId, agora));
        return Result.Ok();
    }

    /// <summary>Saga: Pagamentos confirmou o PIX. Idempotente (o evento pode chegar duas vezes).</summary>
    public Result RegistrarDesembolsoConcluido(DateTimeOffset agora)
    {
        if (Status == StatusProposta.Liberada)
            return Result.Ok();
        if (Status != StatusProposta.Contratada)
            return PropostasErros.TransicaoInvalida(Status, "liberar");
        Status = StatusProposta.Liberada;
        FinalizadaEm = agora;
        _eventos.Add(new PropostaLiberada(Id, ClienteId, agora));
        return Result.Ok();
    }

    /// <summary>Saga, compensação: não saiu dinheiro. Idempotente.</summary>
    public Result RegistrarDesembolsoFalhou(string motivo, DateTimeOffset agora)
    {
        if (Status == StatusProposta.DesembolsoFalhou)
            return Result.Ok();
        if (Status != StatusProposta.Contratada)
            return PropostasErros.TransicaoInvalida(Status, "desfazer a contratação de");
        Status = StatusProposta.DesembolsoFalhou;
        Justificativa = motivo;
        FinalizadaEm = agora;
        _eventos.Add(new ContratacaoDesfeita(Id, ClienteId, motivo, agora));
        return Result.Ok();
    }

    /// <summary>Job de limpeza: aprovações vencidas sem contratação.</summary>
    public bool ExpirarSeVencida(DateTimeOffset agora)
    {
        if (Status != StatusProposta.Aprovada || ValidaAte is not { } limite || agora <= limite)
            return false;
        Expirar(agora);
        return true;
    }

    private void Aprovar(bool automatica, string? analista, DateTimeOffset agora, int validadeDias)
    {
        Status = StatusProposta.Aprovada;
        DecisaoAutomatica = automatica;
        AnalisadoPor = analista;
        DecididaEm = agora;
        ValidaAte = agora.AddDays(validadeDias);
        _eventos.Add(new PropostaAprovada(Id, ClienteId, automatica, analista, ValidaAte.Value, agora));
    }

    private void Recusar(bool automatica, string? analista, string codigo, DateTimeOffset agora)
    {
        Status = StatusProposta.Recusada;
        DecisaoAutomatica = automatica;
        AnalisadoPor = analista;
        CodigoMotivo = codigo;
        DecididaEm = agora;
        FinalizadaEm = agora;
        _eventos.Add(new PropostaRecusada(Id, ClienteId, automatica, codigo, analista, agora));
    }

    private void Expirar(DateTimeOffset agora)
    {
        Status = StatusProposta.Expirada;
        FinalizadaEm = agora;
        _eventos.Add(new PropostaExpirada(Id, ClienteId, agora));
    }
}

/// <summary>Uma parcela como a simulação calculou (valor; o vencimento só existe depois de contratar).</summary>
public sealed record ParcelaPrevista(int Numero, long ValorCentavos);

/// <summary>O que a proposta copia da simulação.</summary>
public sealed record CondicoesDaSimulacao(
    Guid SimulacaoId,
    Guid? ClienteId,
    Dinheiro ValorSolicitado,
    Dinheiro ValorLiberado,
    int PrazoMeses,
    Percentual TaxaMensal,
    Percentual CetAnual,
    string Sistema,
    IReadOnlyList<ParcelaPrevista> Parcelas,
    DateTimeOffset RealizadaEm);
