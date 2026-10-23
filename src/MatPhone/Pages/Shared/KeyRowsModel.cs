using MatPhone.Business;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages;

/// <summary>
/// What the editor of function keys (_KeyRows) needs: the id of the control (several can share a page), the prefix of the posted fields ("Input.Keys"), the keys
/// and the choices of colleagues and groups.
/// </summary>
public sealed record KeyRowsModel(string Id, string Prefix, List<FunctionKeyInput> Keys, List<SelectListItem> People, List<SelectListItem> Groups);
