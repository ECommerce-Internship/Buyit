using System;
using System.Collections.Generic;
using System.Text;
using Buyit.Domain.Entities;

namespace Buyit.Application.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);
}

