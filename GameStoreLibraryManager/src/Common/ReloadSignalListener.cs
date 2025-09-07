using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace GameStoreLibraryManager.Common
{
    public static class ReloadSignalListener
    {
        private const string PipeName = "GSLM_ReloadSignalPipe";

        public static async Task<string> WaitForSignalAsync(SimpleLogger logger)
        {
            var pipeName = $"{PipeName}_{Environment.UserName}";
            logger.Log($"[ReloadSignalListener] Creating Named Pipe server: {pipeName}");

            try
            {
                using (var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    logger.Log("[ReloadSignalListener] Waiting for connection...");

                    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    await server.WaitForConnectionAsync(cancellationTokenSource.Token);

                    logger.Log("[ReloadSignalListener] Client connected. Reading message...");

                    using (var reader = new StreamReader(server))
                    {
                        var message = await reader.ReadToEndAsync();
                        logger.Log($"[ReloadSignalListener] Message received: {message}");
                        return message;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger.Log("[ReloadSignalListener] Timed out waiting for signal after 60 seconds.");
                return null;
            }
            catch (Exception ex)
            {
                logger.Log($"[ReloadSignalListener] An error occurred: {ex.Message}");
                return null;
            }
        }
    }
}
