namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Falha que NÃO melhora com nova tentativa: evento malformado, versão não suportada, regra que nunca vai passar.
/// O consumidor manda direto para a DLT, confirma e segue, para não travar a partição.
/// </summary>
public class ErroPermanenteException(string mensagem, Exception? interna = null) : Exception(mensagem, interna);

/// <summary>Falha que provavelmente passa sozinha (dependência fora, timeout). O consumidor repete, nunca manda para a DLT.</summary>
public class ErroTransitorioException(string mensagem, Exception? interna = null) : Exception(mensagem, interna);
