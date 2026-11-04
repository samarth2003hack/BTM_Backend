using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using EMSAPI.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpClient();

// Every endpoint runs through TokenAuthFilter, which requires a token matching
// the caller's UserMaster.TokenNo. Endpoints marked [SkipTokenCheck] — sign-in,
// and nothing else — are exempt. See Models/TokenAuthFilter.cs.
builder.Services.AddControllers(options =>
{
    options.Filters.Add<TokenAuthFilter>();
});

builder.Services.AddDbContext<EMSContext>(x => x.UseSqlServer(builder.Configuration.GetConnectionString("EMSConnection")));

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // "Authorize" in the Swagger UI asks for one value — the token — and sends
    // it on every try-it-out call. That single value is the whole credential:
    // the server matches it against UserMaster.TokenNo to find the caller.
    options.AddSecurityDefinition("Token", new OpenApiSecurityScheme
    {
        Name = TokenAuthFilter.TokenHeader,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Token returned by /api/Home/login. Paste it here — nothing else is needed."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Token" } }] = Array.Empty<string>()
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
});

builder.Services.AddMemoryCache();

// Who is signed in right now, so a browser that lost its session to a newer
// sign-in can be told what happened instead of just failing. Singleton: the
// register is the process's, not a request's. See Models/UserSessionRegistry.cs.
builder.Services.AddSingleton<UserSessionRegistry>();

builder.WebHost.UseWebRoot("wwwroot");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var headers = ctx.Context.Response.Headers;

            headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
        }
    }
});

app.UseAuthorization();

app.MapControllers();

app.Run();
