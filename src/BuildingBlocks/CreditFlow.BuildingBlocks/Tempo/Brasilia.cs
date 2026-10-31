namespace CreditFlow.BuildingBlocks.Tempo;

/// <summary>
/// "Hoje" para o negócio é o dia em Brasília, não o dia UTC do contêiner: às 22h de Brasília
/// já é amanhã em UTC, e a data de contratação (que conta os dias do IOF) sairia errada.
/// </summary>
public static class Brasilia
{
    // O Brasil não tem horário de verão desde 2019; se a base de fusos não existir na imagem
    // (chiseled sem "-extra"), cair para UTC−3 fixo é correto hoje e não derruba a aplicação.
    private static readonly TimeZoneInfo Fuso = Carregar();

    public static DateTimeOffset Agora(TimeProvider relogio)
    {
        ArgumentNullException.ThrowIfNull(relogio);
        return TimeZoneInfo.ConvertTime(relogio.GetUtcNow(), Fuso);
    }

    public static DateOnly Hoje(TimeProvider relogio) => DateOnly.FromDateTime(Agora(relogio).DateTime);

    private static TimeZoneInfo Carregar()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
        }
    }
}
