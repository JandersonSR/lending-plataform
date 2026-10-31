using CreditFlow.BuildingBlocks;

namespace CreditFlow.Modules.Clientes.Domain;

/// <summary>Os erros previstos do módulo, com código estável ("clientes.xxx"). O front depende do código.</summary>
public static class ClientesErros
{
    public static readonly Erro NomeObrigatorio =
        Erro.Validacao("clientes.nome_obrigatorio", "O nome é obrigatório (2 a 120 caracteres).", "nome");

    public static readonly Erro RendaInvalida =
        Erro.Validacao("clientes.renda_invalida", "A renda mensal deve ser maior que zero e ter no máximo 2 casas.", "rendaMensal");

    public static readonly Erro ConsentimentoObrigatorio =
        Erro.Regra("clientes.consentimento_obrigatorio",
            "Para cadastrar, é preciso autorizar o uso dos dados para análise de crédito.");

    public static readonly Erro VersaoDoTermoObrigatoria =
        Erro.Validacao("clientes.versao_termo", "Informe a versão do termo aceito.", "versaoDoTermo");

    public static readonly Erro CpfJaCadastrado =
        Erro.Conflito("clientes.cpf_ja_cadastrado", "Já existe um cliente com este CPF.");

    public static readonly Erro UsuarioJaCadastrado =
        Erro.Conflito("clientes.usuario_ja_cadastrado", "Este usuário já tem um cadastro de cliente.");

    public static readonly Erro NaoEncontrado =
        Erro.NaoEncontrado("clientes.nao_encontrado", "Cliente não encontrado.");

    public static readonly Erro FinalidadeInvalida =
        Erro.Validacao("clientes.finalidade_invalida", "Finalidade desconhecida. Use AnaliseDeCredito ou OfertasPorEmail.", "finalidade");
}
