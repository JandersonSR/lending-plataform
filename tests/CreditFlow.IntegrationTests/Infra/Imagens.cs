namespace CreditFlow.IntegrationTests.Infra;

/// <summary>As mesmas versões do deploy/local/docker-compose.yml. Mudou lá, muda aqui.</summary>
internal static class Imagens
{
    public const string Postgres = "postgres:17-alpine";
    public const string Mongo = "mongo:8.0";
    public const string Keycloak = "quay.io/keycloak/keycloak:26.8.1";
    public const string Dragonfly = "docker.dragonflydb.io/dragonflydb/dragonfly:v2.0.2";
}
