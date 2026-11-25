namespace Admin.Web;

public static class ErrorHandlingExtensions
{
    public const string ErrorPath = "/Home/Error";
    public const string StatusCodePathFormat = "/Home/StatusCode/{0}";

    public static WebApplication UseErrorPages(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            return app;
        }

        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandlingPath = ErrorPath,
            SuppressDiagnosticsCallback = _ => true
        });
        app.UseStatusCodePagesWithReExecute(StatusCodePathFormat);
        app.UseHsts();

        return app;
    }
}
