using System;                        // Importa tipos básicos do .NET (Exception, String, etc.)
using System.Data.SqlClient;          // Fornece classes para conexão e execução de comandos SQL no SQL Server
using System.Data.SqlTypes;           // Contém tipos SQL nativos usados por procedimentos CLR (SqlString, SqlInt32, etc.)
using System.Threading;               // Necessário para criar e gerenciar threads manuais
using Microsoft.SqlServer.Server;     // Contém atributos e classes específicos para integração CLR no SQL Server

/// <summary>
/// Assembly CLR que expõe uma stored procedure para execução assíncrona de comandos T-SQL.
///
/// Problema resolvido:
///   No SQL Server, procedures bloqueiam a transação chamante até concluir.
///   Com este CLR, o comando é disparado em uma thread do ThreadPool e a procedure
///   retorna imediatamente — o chamador não fica bloqueado esperando o resultado.
///
/// Caso de uso típico:
///   Triggers ou procedures OLTP que precisam disparar um job sem atrasar a transação.
///   Ex: INSERT que precisa enviar uma notificação a um serviço externo em background.
///
/// Registro no SQL Server:
///   CREATE ASSEMBLY SQLProcedureAssincrona FROM '...\SQLProcedureAssincrona.dll' WITH PERMISSION_SET = UNSAFE;
///   CREATE PROCEDURE dbo.ExecuteAsync @databaseName NVARCHAR(128), @commandText NVARCHAR(MAX), @Timeout INT = 10
///   AS EXTERNAL NAME SQLProcedureAssincrona.[SQLProcedureAssincrona].ExecuteAsync;
///
/// ⚠️ Requer PERMISSION_SET = UNSAFE para acesso à rede (conexões externas via SqlConnection).
/// </summary>
public class SQLProcedureAssincrona
{
    /// <summary>
    /// Executa um comando T-SQL em background, sem bloquear a transação chamante.
    /// </summary>
    /// <param name="databaseName">Nome do banco de dados onde o comando será executado.</param>
    /// <param name="commandText">Comando T-SQL a ser executado (ex: EXEC dbo.MinhaProc).</param>
    /// <param name="Timeout">Timeout em segundos para a conexão e o comando. Padrão: 10s.</param>
    [SqlProcedure]
    public static void ExecuteAsync(SqlString databaseName, SqlString commandText, int Timeout = 10)
    {
        // Validação de entrada — parâmetros nulos causariam exceção na thread de background
        // e o erro seria silencioso (sem retorno ao chamador).
        if (databaseName.IsNull || commandText.IsNull) return;

        // Copia os valores para variáveis locais antes de entrar na thread.
        // SqlString não é thread-safe e pode ser coletada pelo GC se referenciarmos
        // o parâmetro diretamente de dentro do delegate.
        string db = databaseName.Value;
        string cmd = commandText.Value;

        // Em vez de 'new Thread()', usamos o ThreadPool.
        // É mais leve e o CLR do SQL lida um pouco melhor com ele.
        ThreadPool.QueueUserWorkItem(state =>
        {
            // ⚠️ CONFIGURAÇÃO DA CONNECTION STRING
            // Nunca inclua servidor, usuário ou senha diretamente no código.
            // Configure as variáveis de ambiente no serviço do SQL Server:
            //   setx SQL_CLR_SERVER   "SEU_SERVIDOR_OU_IP"
            //   setx SQL_CLR_USER     "usuario_sql"
            //   setx SQL_CLR_PASSWORD "senha_sql"
            // Alternativamente, use Windows Authentication e remova User Id/Password.
            string sqlServer   = Environment.GetEnvironmentVariable("SQL_CLR_SERVER")   ?? "SQL_CLR_SERVER_NAO_CONFIGURADO";
            string sqlUser     = Environment.GetEnvironmentVariable("SQL_CLR_USER")     ?? "SQL_CLR_USER_NAO_CONFIGURADO";
            string sqlPassword = Environment.GetEnvironmentVariable("SQL_CLR_PASSWORD") ?? "SQL_CLR_PASSWORD_NAO_CONFIGURADO";

            try
            {
                // Timeout curtíssimo! Se o servidor remoto não responder rápido,
                // a thread deve morrer logo para não virar um "zumbi".
                string connString = $"Server={sqlServer};Database={db};User Id={sqlUser};Password={sqlPassword};Connect Timeout={Timeout};Pooling=false;";

                using (SqlConnection conn = new SqlConnection(connString))
                {
                    conn.Open();
                    using (SqlCommand sqlCmd = new SqlCommand(cmd, conn))
                    {
                        sqlCmd.CommandTimeout = Timeout; // No máximo 10 segundos
                        sqlCmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    // Precisamos de uma nova conexão para gravar o erro,
                    // já que a thread está "orfã" do processo principal.
                    string connString = $"Server={sqlServer};Database={db};User Id={sqlUser};Password={sqlPassword};Connect Timeout=10;Pooling=false;";
                    using (SqlConnection errConn = new SqlConnection(connString))
                    {
                        errConn.Open();
                        string logCmd = "INSERT INTO _sys..Notificacoes (connectionString, Local, msg) VALUES (@db, @loc, @msg)";
                        using (SqlCommand cmdErr = new SqlCommand(logCmd, errConn))
                        {
                            cmdErr.Parameters.AddWithValue("@db", db);
                            cmdErr.Parameters.AddWithValue("@loc", "CLR_THREAD_FATAL_ERROR");
                            cmdErr.Parameters.AddWithValue("@msg", "Erro crítico na Thread: " + ex.Message + " | " + ex.StackTrace + " | "+ commandText);
                            cmdErr.ExecuteNonQuery();
                        }
                    }
                }
                catch
                {
                    // Se até o log falhar, escrevemos no log do Windows como última instância
                    System.Diagnostics.EventLog.WriteEntry("SQL-CLR", "Erro ao tentar logar no banco: " + ex.ToString());
                }
            }
        });
    }
}
