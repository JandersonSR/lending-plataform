using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CreditFlow.BuildingBlocks.Mensageria;

public enum TipoDeFalha
{
    /// <summary>Passa sozinha (banco fora, timeout, conflito de concorrência). Repetir a MESMA mensagem, sem avançar.</summary>
    Transitoria,

    /// <summary>Nunca vai passar (JSON inválido, versão desconhecida, regra violada). DLT na hora.</summary>
    Permanente,

    /// <summary>Não sabemos (bug?). Repete algumas vezes; se persistir, DLT, para não travar a partição para sempre.</summary>
    Desconhecida,
}

/// <summary>
/// Classificar a falha é a decisão mais importante do consumidor: tratar tudo como transitório
/// trava a partição num bug; tratar tudo como permanente manda para a DLT tudo o que chegou
/// enquanto o banco estava fora.
/// </summary>
public static class ClassificadorDeFalhas
{
    public static TipoDeFalha Classificar(Exception ex)
    {
        for (var atual = ex; atual is not null; atual = atual.InnerException)
        {
            switch (atual)
            {
                case ErroPermanenteException:
                case JsonException:
                case NotSupportedException:
                    return TipoDeFalha.Permanente;
                case ErroTransitorioException:
                case TimeoutException:
                case HttpRequestException:
                case DbUpdateConcurrencyException:      // outro processo mudou o agregado: reler e tentar de novo
                case RetryLimitExceededException:       // a estratégia do EF desistiu: o banco ainda está fora
                case DbException { IsTransient: true }: // Npgsql marca rede, failover, deadlock, serialization failure
                    return TipoDeFalha.Transitoria;
            }
        }
        return TipoDeFalha.Desconhecida;
    }
}
