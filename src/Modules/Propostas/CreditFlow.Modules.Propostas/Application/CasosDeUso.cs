using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.Auth;
using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Clientes.Contracts;
using CreditFlow.Modules.Propostas.Domain;
using Microsoft.Extensions.Options;

namespace CreditFlow.Modules.Propostas.Application;

public sealed record CriarPropostaComando(Guid ClienteId, Guid SimulacaoId);

public sealed class CriarProposta(
    IPropostaRepositorio repositorio,
    ISimulacoesParaProposta simulacoes,
    IConsultaDeClientes clientes,
    ICurrentUser usuario,
    IOptions<ParametrosDeAnalise> parametros,
    TimeProvider relogio)
{
    public async Task<Result<Proposta>> ExecutarAsync(CriarPropostaComando comando, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comando);
        var solicitante = usuario.Id ?? throw new InvalidOperationException("CriarProposta exige usuário autenticado (policy).");

        var condicoes = await simulacoes.ObterAsync(comando.SimulacaoId, ct);
        if (condicoes is null)
            return PropostasErros.SimulacaoNaoEncontrada;

        var perfil = await clientes.ObterPerfilDeCreditoAsync(comando.ClienteId, ct);
        if (perfil is null)
            return PropostasErros.ClienteNaoEncontrado;

        var proposta = Proposta.Criar(
            Guid.CreateVersion7(), perfil.ClienteId, solicitante, condicoes,
            perfil.Score, Dinheiro.DeCentavos(perfil.RendaMensalCentavos), perfil.ConsentimentoAnaliseDeCredito,
            parametros.Value, relogio.GetUtcNow());
        if (proposta.Falhou)
            return proposta.Erro;

        // Sem "SELECT para ver se já tem proposta ativa": entre o SELECT e o INSERT outra requisição
        // passaria. Quem decide é o índice único parcial ux_propostas_cliente_ativa.
        if (!await repositorio.AdicionarAsync(proposta.Valor, ct))
            return PropostasErros.JaExistePropostaAtiva;

        return proposta;
    }
}

public sealed class ConsultarProposta(IPropostaRepositorio repositorio, ICurrentUser usuario)
{
    public async Task<Result<Proposta>> ExecutarAsync(Guid id, CancellationToken ct)
    {
        var proposta = await repositorio.ObterAsync(id, ct);
        // Regra de recurso: cliente só vê a própria. 404 (e não 403) para não confirmar que o id existe.
        if (proposta is null || !PodeVer(usuario, proposta))
            return PropostasErros.NaoEncontrada;
        return proposta;
    }

    internal static bool PodeVer(ICurrentUser usuario, Proposta proposta) =>
        usuario.TemPapel(Papeis.Analista) || usuario.TemPapel(Papeis.Admin) || proposta.SolicitanteId == usuario.Id;
}

public sealed class ListarPropostas(IPropostaRepositorio repositorio, ICurrentUser usuario)
{
    public Task<IReadOnlyList<Proposta>> MinhasAsync(CancellationToken ct) =>
        repositorio.ListarDoSolicitanteAsync(usuario.Id ?? "", 50, ct);

    public Task<IReadOnlyList<Proposta>> FilaDeAnaliseAsync(CancellationToken ct) =>
        repositorio.ListarPorStatusAsync(StatusProposta.EmAnalise, 100, ct);
}

public enum AcaoDoAnalista { Aprovar, Recusar }

public sealed class DecidirProposta(
    IPropostaRepositorio repositorio, ICurrentUser usuario, IOptions<ParametrosDeAnalise> parametros, TimeProvider relogio)
{
    /// <param name="versaoVista">Versão (ETag) que o analista tinha na tela. Null = sem If-Match.</param>
    public async Task<Result<Proposta>> ExecutarAsync(Guid id, AcaoDoAnalista acao, string? texto, uint? versaoVista, CancellationToken ct)
    {
        var analista = usuario.Nome ?? usuario.Id ?? throw new InvalidOperationException("Sem usuário autenticado.");
        var proposta = await repositorio.ObterAsync(id, ct);
        if (proposta is null)
            return PropostasErros.NaoEncontrada;

        // Atalho: se a versão já é outra na leitura, nem tenta (o banco confirmaria o conflito de qualquer jeito).
        if (versaoVista is { } v && v != proposta.Versao)
            return PropostasErros.AlteradaPorOutro;

        var agora = relogio.GetUtcNow();
        var resultado = acao == AcaoDoAnalista.Aprovar
            ? proposta.AprovarManualmente(analista, texto, agora, parametros.Value.ValidadeDaAprovacaoDias)
            : proposta.RecusarManualmente(analista, texto, agora);
        if (resultado.Falhou)
            return resultado.Erro;

        // Dois analistas na mesma proposta: os dois leram EmAnalise; o primeiro grava e muda o xmin;
        // o UPDATE do segundo (WHERE xmin = <o que ele leu>) afeta 0 linhas e vira conflito.
        if (!await repositorio.SalvarAsync(proposta, versaoVista, ct))
            return PropostasErros.AlteradaPorOutro;
        return proposta;
    }
}

public sealed class ContratarProposta(IPropostaRepositorio repositorio, ICurrentUser usuario, TimeProvider relogio)
{
    public async Task<Result<Proposta>> ExecutarAsync(Guid id, string? chavePix, CancellationToken ct)
    {
        var proposta = await repositorio.ObterAsync(id, ct);
        if (proposta is null || proposta.SolicitanteId != usuario.Id)
            return PropostasErros.NaoEncontrada;

        var resultado = proposta.Contratar(chavePix, relogio.GetUtcNow());
        if (resultado.Falhou)
        {
            // Contratar tarde demais expira a proposta: essa mudança também precisa ser gravada.
            if (proposta.Status == StatusProposta.Expirada)
                await repositorio.SalvarAsync(proposta, null, ct);
            return resultado.Erro;
        }

        // Duplo clique no "Contratar": a segunda requisição leu Aprovada, mas o xmin mudou: conflito, nada duplica.
        if (!await repositorio.SalvarAsync(proposta, null, ct))
            return PropostasErros.AlteradaPorOutro;
        return proposta;
    }
}

public sealed class CancelarProposta(IPropostaRepositorio repositorio, ICurrentUser usuario, TimeProvider relogio)
{
    public async Task<Result<Proposta>> ExecutarAsync(Guid id, CancellationToken ct)
    {
        var proposta = await repositorio.ObterAsync(id, ct);
        if (proposta is null || proposta.SolicitanteId != usuario.Id)
            return PropostasErros.NaoEncontrada;

        var resultado = proposta.Cancelar(relogio.GetUtcNow());
        if (resultado.Falhou)
            return resultado.Erro;
        if (!await repositorio.SalvarAsync(proposta, null, ct))
            return PropostasErros.AlteradaPorOutro;
        return proposta;
    }
}

/// <summary>
/// Passos da saga chamados pelos consumidores de DesembolsoConcluido/DesembolsoFalhou (módulo 6).
/// Idempotentes no agregado; em conflito de concorrência, relê e tenta de novo.
/// </summary>
public sealed class RegistrarResultadoDoDesembolso(IPropostaRepositorio repositorio, TimeProvider relogio)
{
    public Task<Result> ConcluidoAsync(Guid propostaId, CancellationToken ct) =>
        AplicarAsync(propostaId, (p, agora) => p.RegistrarDesembolsoConcluido(agora), ct);

    public Task<Result> FalhouAsync(Guid propostaId, string motivo, CancellationToken ct) =>
        AplicarAsync(propostaId, (p, agora) => p.RegistrarDesembolsoFalhou(motivo, agora), ct);

    private async Task<Result> AplicarAsync(Guid propostaId, Func<Proposta, DateTimeOffset, Result> passo, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var proposta = await repositorio.ObterAsync(propostaId, ct);
            if (proposta is null)
                return PropostasErros.NaoEncontrada;

            var resultado = passo(proposta, relogio.GetUtcNow());
            if (resultado.Falhou || proposta.EventosDeDominio.Count == 0)
                return resultado; // falhou, ou já estava aplicado (idempotência): nada a gravar

            if (await repositorio.SalvarAsync(proposta, null, ct))
                return Result.Ok();
            if (tentativa == 3)
                return PropostasErros.AlteradaPorOutro;
        }
    }
}
