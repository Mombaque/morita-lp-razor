using System.Text.Json;

namespace Morita.LP.Razor.Services;

public record FaqItem(string Question, string Answer);

public static class FaqContent
{
    public static readonly IReadOnlyList<FaqItem> Items =
    [
        new("Como sei qual tamanho de kimono escolher?",
            "Use a calculadora de kimono ou informe altura e peso na consulta. A tabela é aproximada e varia por marca, por isso a Morita confirma o tamanho ideal com você pelo WhatsApp."),
        new("Posso retirar na loja?",
            $"Sim. A loja fica na {StoreInfo.Street}, em {StoreInfo.City}. Atendemos de segunda a sexta, das 9h às 18h, e aos sábados, das 9h às 13h."),
        new("Vocês fazem envio?",
            "Sim. Atendemos Sorocaba e região com retirada na loja ou envio. Informe seu bairro na consulta e combinamos o prazo e o valor pelo WhatsApp."),
        new("Como funciona a consulta pelo site?",
            "Você escolhe os produtos, tamanhos e cores e envia seu nome e WhatsApp. A equipe da Morita responde com disponibilidade e valores. A consulta não é uma compra e não gera cobrança."),
        new("Vocês atendem equipes e academias?",
            "Sim. Fazemos orçamento para pedidos em quantidade, como kimonos e faixas para graduação ou uniformes de equipe. Fale com a gente pelo WhatsApp."),
        new("Tem kimono infantil?",
            "Sim. Temos kimonos infantis de Jiu-Jitsu e opções para Judô, do M000 ao M4, além de faixas infantis de todas as graduações."),
    ];

    public static string JsonLd => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "FAQPage",
        ["mainEntity"] = Items.Select(item => new Dictionary<string, object>
        {
            ["@type"] = "Question",
            ["name"] = item.Question,
            ["acceptedAnswer"] = new Dictionary<string, string>
            {
                ["@type"] = "Answer",
                ["text"] = item.Answer,
            },
        }),
    });
}
