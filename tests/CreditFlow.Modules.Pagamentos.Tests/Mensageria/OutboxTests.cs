using System.Text.Json;
using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.Modules.Pagamentos.Contracts.Eventos;
using CreditFlow.Modules.Propostas.Contracts.Eventos;

namespace CreditFlow.Modules.Pagamentos.Tests.Mensageria;

public class OutboxTests
{
    // Valores de referência calculados por uma implementação independente de FNV-1a 32 bits (Node.js,
    // a mesma do protótipo do relay usado no curso). Se este teste quebrar, a faixa de uma chave mudou
    // entre versões: mensagens pendentes da mesma chave poderiam sair fora de ordem.
    [Theory]
    [InlineData("a", 12)]
    [InlineData("proposta-1", 7)]
    [InlineData("0190f0a2-7c1e-7b3d-9a4f-2c8e5d1b6a70", 4)]
    [InlineData("ação", 9)]
    public void Faixa_e_estavel_entre_processos_e_linguagens(string chave, short faixaEsperada) =>
        Assert.Equal(faixaEsperada, MensagemOutbox.FaixaDa(chave));

    [Fact]
    public void Criar_preenche_tipo_versao_topico_e_faixa_do_atributo()
    {
        var agora = DateTimeOffset.UtcNow;
        var evento = new DesembolsoSolicitado(Guid.CreateVersion7(), agora, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100);
        var m = MensagemOutbox.Criar(evento, evento.PropostaId.ToString(), agora);

        Assert.Equal("DesembolsoSolicitado", m.Tipo);
        Assert.Equal(1, m.Versao);
        Assert.Equal(Topicos.Pagamentos, m.Topico);
        Assert.Equal(evento.EventoId, m.EventoId);
        Assert.Equal(MensagemOutbox.FaixaDa(evento.PropostaId.ToString()), m.Faixa);
        Assert.Null(m.PublicadaEm);
        Assert.Contains("\"valorCentavos\":100", m.Conteudo, StringComparison.Ordinal);
    }

    [Fact]
    public void Leitor_tolerante_ignora_campo_novo_do_produtor()
    {
        // O produtor acrescentou "canal" (mudança aditiva, mesma versão): o consumidor antigo não quebra.
        const string json = """
            {"eventoId":"0190f0a2-7c1e-7b3d-9a4f-2c8e5d1b6a70","ocorridoEm":"2026-10-30T12:00:00+00:00",
             "propostaId":"0190f0a2-7c1e-7b3d-9a4f-2c8e5d1b6a71","clienteId":"0190f0a2-7c1e-7b3d-9a4f-2c8e5d1b6a72",
             "usuarioId":"sub-1","valorLiquidoCentavos":500000,"chavePix":"x@pix.dev","canal":"app",
             "parcelas":[{"numero":1,"vencimento":"2026-11-30","valorCentavos":45678}]}
            """;
        var e = (ContratoAssinado)SerializadorDeEventos.Desserializar(System.Text.Encoding.UTF8.GetBytes(json), typeof(ContratoAssinado));
        Assert.Equal(500_000, e.ValorLiquidoCentavos);
        Assert.Equal(new DateOnly(2026, 11, 30), e.Parcelas[0].Vencimento);
    }

    [Fact]
    public void Enum_trafega_como_texto()
    {
        var e = new DesembolsoFalhou(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            MotivoFalhaDesembolso.GatewayIndisponivel, "x", 6);
        var json = SerializadorDeEventos.Serializar(e, typeof(DesembolsoFalhou));
        Assert.Contains("\"motivo\":\"GatewayIndisponivel\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_invalido_e_falha_permanente()
    {
        var ex = Record.Exception(() => SerializadorDeEventos.Desserializar("{nao e json"u8, typeof(ContratoAssinado)));
        Assert.IsAssignableFrom<JsonException>(ex);
        Assert.Equal(TipoDeFalha.Permanente, ClassificadorDeFalhas.Classificar(ex));
    }

    [Fact]
    public void Evento_sem_atributo_nao_pode_ser_publicado() =>
        Assert.Throws<InvalidOperationException>(() => DescritorDeEvento.De<EventoSemAtributo>());

    private sealed record EventoSemAtributo(Guid EventoId, DateTimeOffset OcorridoEm) : CreditFlow.BuildingBlocks.IEventoIntegracao;
}
