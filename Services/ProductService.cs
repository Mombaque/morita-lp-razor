using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public class ProductService
{
    private static readonly List<Product> JiuJitsuProducts = new()
    {
        new Product
        {
            Nome = "Faixas de Jiu-Jitsu",
            Slug = "faixas",
            RequestProductType = "Faixa adulto",
            Alt = "Faixas de Jiu-Jitsu de todas as cores: branca, azul, roxa, marrom, preta. Modelos para adulto e infantil.",
            Descricao = "Faixas de Jiu-Jitsu branca, azul, roxa, marrom e preta, em tamanhos adulto e infantil. Modelos com costura reforçada das marcas In The Guard, Venum e Naja.",
            Imagens = new() { "/images/faixas/1.webp" }
        },
        new Product
        {
            Nome = "Kimono Infantil",
            Slug = "kimono-infantil",
            RequestProductType = "Kimono Infantil / Judô",
            Alt = "Kimono Infantil In The Guard",
            Descricao = "Kimono infantil de Jiu-Jitsu leve, confortável e resistente para treinos do dia a dia. Disponível em diversas cores para crianças que estão começando ou evoluindo no tatame.",
            Imagens = new()
            {
                "/images/kimono/infantil/azul.webp",
                "/images/kimono/infantil/preto.webp",
                "/images/kimono/infantil/branco.webp"
            }
        },
        new Product
        {
            Nome = "Kimono Adulto",
            Slug = "kimono-adulto",
            RequestProductType = "Kimono Adulto",
            Alt = "Kimono Adulto In The Guard",
            Descricao = "Kimono adulto de Jiu-Jitsu das marcas In The Guard, South Team, Naja e Keiko. Modelos leves, resistentes, com tecido trançado e opções para treino ou competição em Sorocaba.",
            Imagens = new()
            {
                "/images/kimono/adulto/itg-chumbo.webp",
                "/images/kimono/adulto/itg-azul.webp",
                "/images/kimono/adulto/itg-branco.webp",
                "/images/kimono/adulto/itg-marinho.webp",
                "/images/kimono/adulto/st-preto.webp",
                "/images/kimono/adulto/naja-azul.webp"
            }
        },
        new Product
        {
            Nome = "Rashguard Masculina",
            Slug = "rashguard-masculina",
            RequestProductType = "Rashguard",
            Alt = "Rashguard masculina In The Guard",
            Descricao = "Rashguard masculina para Jiu-Jitsu, grappling e no-gi, com tecido de compressão e liberdade de movimento. Modelos das marcas In The Guard, Venum e outras opções para treino.",
            Imagens = new()
            {
                "/images/rashguard/masculino/venum1.webp",
                "/images/rashguard/masculino/venum2.webp",
                "/images/rashguard/masculino/venum3.webp",
                "/images/rashguard/masculino/venum4.webp",
                "/images/rashguard/masculino/itg1.webp",
                "/images/rashguard/masculino/itg2.webp",
                "/images/rashguard/masculino/itg3.webp"
            }
        },
        new Product
        {
            Nome = "Rashguard Feminina",
            Slug = "rashguard-feminina",
            RequestProductType = "Rashguard",
            Alt = "Rashguard feminina In The Guard",
            Descricao = "Rashguard feminina para Jiu-Jitsu sem kimono, no-gi e treinos de alta intensidade. Tecido confortável, com compressão e bom ajuste para movimentação no tatame.",
            Imagens = new()
            {
                "/images/rashguard/feminino/1.webp",
                "/images/rashguard/feminino/2.webp"
            }
        }
    };

    private static readonly List<Product> MuayThaiProducts = new()
    {
        new Product
        {
            Nome = "Luva de Muay Thai / Boxe",
            Slug = "luvas",
            RequestProductType = "Luvas",
            Alt = "Luva de Boxe e Muay Thai",
            Descricao = "Luvas para Muay Thai e Boxe, indicadas para treinos e competições. Disponíveis em 12oz, 14oz e 16oz.",
            Imagens = new()
            {
                "/images/muay-thai/luva-st.webp",
                "/images/muay-thai/luva-preta-st.webp",
                "/images/muay-thai/luva-vermelha-st.webp"
            }
        },
        new Product
        {
            Nome = "Shorts de Muay Thai",
            Slug = "shorts",
            RequestProductType = "Bermuda / shorts",
            Alt = "Shorts de Muay Thai",
            Descricao = "Shorts de Muay Thai com design tailandês, tecido leve e liberdade de movimento para chutes e joelhadas. Modelos resistentes para treino em Sorocaba.",
            Imagens = new()
            {
                "/images/muay-thai/short-amarelo.webp",
                "/images/muay-thai/short-camuflado.webp",
                "/images/muay-thai/short-rosa.webp",
                "/images/muay-thai/short-thailandia.webp",
                "/images/muay-thai/short-vermelho.webp",
                "/images/muay-thai/shorts-dragao.webp"
            }
        }
    };

    public List<Product> GetJiuJitsuProducts() => JiuJitsuProducts;
    public List<Product> GetMuayThaiProducts() => MuayThaiProducts;
}
