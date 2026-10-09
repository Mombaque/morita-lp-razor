namespace Morita.LP.Razor.Models;

public static class EmbeddedCardFailureMessages
{
    public static string For(string? reason) => reason switch
    {
        "insufficient_funds" => "O cartão não tem limite suficiente. Tente outro cartão ou pague com PIX.",
        "invalid_card_number" => "Confira o número do cartão e tente novamente.",
        "invalid_expiration" => "Confira a data de validade do cartão.",
        "invalid_security_code" => "Confira o código de segurança do cartão.",
        "invalid_card_data" or "invalid_payment_data" => "Confira os dados do cartão e tente novamente.",
        "call_for_authorize" => "O emissor pediu autorização. Ligue para o banco do cartão e tente novamente.",
        "card_disabled" => "Este cartão está inativo. Ative-o com o banco ou use outro cartão.",
        "duplicated_payment" => "Já existe um pagamento igual em análise. Aguarde ou use outro cartão.",
        "high_risk" => "O pagamento não foi aprovado pela análise de segurança. Tente outro cartão ou pague com PIX.",
        "max_attempts" => "Limite de tentativas atingido para este cartão. Use outro cartão ou pague com PIX.",
        "installments_not_allowed" => "Este número de parcelas não está disponível. Escolha outra opção.",
        "authentication_failed" => "A autenticação do cartão não foi concluída. Tente novamente.",
        "payment_not_created" => "Não conseguimos confirmar a tentativa com o cartão. Nenhuma cobrança foi feita; tente novamente.",
        _ => "O pagamento com cartão não foi aprovado. Tente outro cartão ou pague com PIX."
    };
}
