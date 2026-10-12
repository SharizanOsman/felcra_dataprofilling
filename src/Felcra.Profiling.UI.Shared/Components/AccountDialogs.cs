using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.UI.Shared.Api;
using Felcra.Profiling.UI.Shared.Auth;
using Felcra.Profiling.UI.Shared.Forms;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Felcra.Profiling.UI.Shared.Components;

/// <summary>Dialogs about the signed-in user's own account, shared by both apps.</summary>
public static class AccountDialogs
{
    private static readonly Func<object, string, Task<IEnumerable<string>>> Validate = FormValidation.For(new ChangePasswordRequestValidator());

    /// <summary>Returns true when the password was changed (the new tokens are already stored).</summary>
    public static Task<bool> ChangePasswordAsync(this IDialogService dialogs, ApiClient api, AuthClient auth) =>
        dialogs.EditAsync(T("Change password"), new ChangePasswordRequest(),
            model => builder =>
            {
                builder.OpenComponent<ChangePasswordFields>(0);
                builder.AddAttribute(1, nameof(ChangePasswordFields.Model), model);
                builder.CloseComponent();
            },
            async model =>
            {
                var result = await api.PostAsync<TokenResponse>("api/auth/change-password", model);
                if (result.Value is { } tokens) await auth.SaveTokensAsync(tokens);
                return result;
            },
            Validate);
}
