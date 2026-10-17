using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class TermosModel(IStoreInfoClient storeInfo) : LegalPageModel(storeInfo);
