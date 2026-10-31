namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Marca um record como evento de integração publicável: nome estável (não o nome da classe .NET,
/// que pode mudar num refactor), versão do contrato e tópico onde ele vai.
/// <code>[EventoIntegracao("ContratoAssinado", 1, Topicos.Propostas)]</code>
/// Regra de versão: acrescentar campo opcional = mesma versão (o leitor ignora o que não conhece);
/// renomear, remover, mudar tipo ou significado = nova versão (novo record, ex.: ContratoAssinadoV2).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventoIntegracaoAttribute(string tipo, int versao, string topico) : Attribute
{
    public string Tipo { get; } = tipo;
    public int Versao { get; } = versao;
    public string Topico { get; } = topico;
}
