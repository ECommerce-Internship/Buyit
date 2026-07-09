using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

public interface IPasswordResetService
{
    Task RequestPasswordResetAsync(ForgotPasswordRequest request);
    Task ResetPasswordAsync(ResetPasswordRequest request);
}
