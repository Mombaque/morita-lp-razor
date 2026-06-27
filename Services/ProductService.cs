using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public class ProductService
{
    private static readonly List<Product> JiuJitsuProducts = new()
    {
        new Product
        {
            Nome = "Faixas de Jiu-Jitsu",
            Alt = "Faixas de Jiu-Jitsu de todas as cores: branca, azul, roxa, marrom, preta. Modelos para adulto e infantil.",
            Descricao = "Faixas de Jiu-Jitsu branca, azul, roxa, marrom e preta, em tamanhos adulto e infantil. Modelos com costura reforçada das marcas In The Guard, Venum e Naja.",
            Imagens = new() { "/images/faixas/1.jpg" }
        },
        new Product
        {
            Nome = "Kimono Infantil",
            Alt = "Kimono Infantil In The Guard",
            Descricao = "Kimono infantil de Jiu-Jitsu leve, confortável e resistente para treinos do dia a dia. Disponível em diversas cores para crianças que estão começando ou evoluindo no tatame.",
            Imagens = new()
            {
                "/images/kimono/infantil/azul.jpeg",
                "/images/kimono/infantil/preto.jpeg",
                "/images/kimono/infantil/branco.jpeg"
            }
        },
        new Product
        {
            Nome = "Kimono Adulto",
            Alt = "Kimono Adulto In The Guard",
            Descricao = "Kimono adulto de Jiu-Jitsu das marcas In The Guard, South Team, Naja e Keiko. Modelos leves, resistentes, com tecido trançado e opções para treino ou competição em Sorocaba.",
            Imagens = new()
            {
                "/images/kimono/adulto/itg-chumbo.png",
                "/images/kimono/adulto/itg-azul.jpg",
                "/images/kimono/adulto/itg-branco.jpeg",
                "/images/kimono/adulto/itg-marinho.png",
                "/images/kimono/adulto/st-preto.jpeg",
                "/images/kimono/adulto/naja-azul.jpeg"
            }
        },
        new Product
        {
            Nome = "Rashguard Masculina",
            Alt = "Rashguard masculina In The Guard",
            Descricao = "Rashguard masculina para Jiu-Jitsu, grappling e no-gi, com tecido de compressão e liberdade de movimento. Modelos das marcas In The Guard, Venum e outras opções para treino.",
            Imagens = new()
            {
                "/images/rashguard/masculino/venum1.jpeg",
                "/images/rashguard/masculino/venum2.jpeg",
                "/images/rashguard/masculino/venum3.jpeg",
                "/images/rashguard/masculino/venum4.jpeg",
                "/images/rashguard/masculino/itg1.png",
                "/images/rashguard/masculino/itg2.jpg",
                "/images/rashguard/masculino/itg3.jpg"
            }
        },
        new Product
        {
            Nome = "Rashguard Feminina",
            Alt = "Rashguard feminina In The Guard",
            Descricao = "Rashguard feminina para Jiu-Jitsu sem kimono, no-gi e treinos de alta intensidade. Tecido confortável, com compressão e bom ajuste para movimentação no tatame.",
            Imagens = new()
            {
                "/images/rashguard/feminino/1.jpeg",
                "/images/rashguard/feminino/2.jpeg"
            }
        }
    };

    private static readonly List<Product> MuayThaiProducts = new()
    {
        new Product
        {
            Nome = "Luva de Muay Thai / Boxe",
            Alt = "Luva de Boxe e Muay Thai",
            Descricao = "Luvas para Muay Thai e Boxe, indicadas para treinos e competições. Disponíveis em 12oz, 14oz e 16oz.",
            Imagens = new()
            {
                "/images/muay-thai/luva-st.jpg",
                "/images/muay-thai/luva-preta-st.jpeg",
                "/images/muay-thai/luva-vermelha-st.jpeg"
            }
        },
        new Product
        {
            Nome = "Shorts de Muay Thai",
            Alt = "Shorts de Muay Thai",
            Descricao = "Shorts de Muay Thai com design tailandês, tecido leve e liberdade de movimento para chutes e joelhadas. Modelos resistentes para treino em Sorocaba.",
            Imagens = new()
            {
                "/images/muay-thai/short-amarelo.png",
                "/images/muay-thai/short-camuflado.png",
                "/images/muay-thai/short-rosa.png",
                "/images/muay-thai/short-thailandia.png",
                "/images/muay-thai/short-vermelho.png",
                "/images/muay-thai/shorts-dragao.png"
            }
        }
    };

    public List<Product> GetJiuJitsuProducts() => JiuJitsuProducts;
    public List<Product> GetMuayThaiProducts() => MuayThaiProducts;
}
