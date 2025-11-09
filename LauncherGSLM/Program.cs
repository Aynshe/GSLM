using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Linq;

namespace LauncherGSLM
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var launcherPath = AppContext.BaseDirectory;
                
                // Logique de recherche améliorée :
                // 1. Chercher dans le même répertoire que le lanceur.
                // 2. Si non trouvé, chercher dans le répertoire parent.
                var gslmPath = Path.Combine(launcherPath, "GameStoreLibraryManager.exe");
                if (!File.Exists(gslmPath))
                {
                    gslmPath = Path.GetFullPath(Path.Combine(launcherPath, "..", "GameStoreLibraryManager.exe"));
                }


                if (!File.Exists(gslmPath))
                {
                    MessageBox.Show($"Erreur: GameStoreLibraryManager.exe non trouvé.\nCherché dans:\n- {Path.Combine(launcherPath, "GameStoreLibraryManager.exe")}\n- {Path.GetFullPath(Path.Combine(launcherPath, "..", "GameStoreLibraryManager.exe"))}", "LauncherGSLM Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return -1;
                }

                // Préparer les arguments pour le processus enfant
                var arguments = string.Join(" ", args.Select(arg => $"\"{arg}\""));

                var startInfo = new ProcessStartInfo
                {
                    FileName = gslmPath,
                    Arguments = arguments,
                    UseShellExecute = false // Important pour une surveillance fiable
                };

                // Lancer le processus et attendre sa fin
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        MessageBox.Show("Erreur: N'a pas pu démarrer le processus GameStoreLibraryManager.exe.", "LauncherGSLM Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return -1;
                    }
                    
                    // Attendre que le processus se termine
                    process.WaitForExit();
                    
                    // Retourner le code de sortie du processus enfant
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Une erreur inattendue est survenue:\n{ex.Message}", "LauncherGSLM Erreur Critique", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return -1;
            }
        }
    }
}
