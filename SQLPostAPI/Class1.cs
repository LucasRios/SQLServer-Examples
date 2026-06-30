using Microsoft.SqlServer.Server; // Namespace necessário para trabalhar com procedimentos armazenados no SQL Server
using System; // Namespace para funcionalidades básicas e comuns do .NET
using System.Data.SqlTypes; // Namespace específico para tipos de dados SQL Server
using System.IO; // Namespace para manipulação de entradas/saídas, como streams e arquivos
using System.Net; // Namespace para criação e manipulação de solicitações HTTP 
using System.Text; 

/// <summary>
/// Assembly CLR que expõe stored procedures para SQL Server realizar chamadas HTTP
/// (GET, POST, FTP upload, WebDAV upload) diretamente do motor do banco.
///
/// Métodos disponíveis:
///   - SQLApiPost          → HTTP POST para APIs REST externas
///   - SQLApiGet           → HTTP GET para consulta a APIs REST externas
///   - SQLFileExists       → Verifica se um arquivo existe em URL via HTTP HEAD
///   - SQLUploadFile       → Upload FTP de conteúdo em Base64 ou hexadecimal
///   - SQLGetFileAndUpload → Download HTTP + upload WebDAV em uma única chamada
///   - SQLUploadFileFacebook → Download de arquivo e envio como multipart/form-data (ex: API Graph)
///
/// Registro no SQL Server (exemplo para SQLApiPost):
///   CREATE ASSEMBLY SQLServerAPI FROM '...\SQLServerAPI.dll' WITH PERMISSION_SET = UNSAFE;
///   CREATE PROCEDURE dbo.SQLApiPost @Url NVARCHAR(2000), @headers NVARCHAR(MAX),
///     @body NVARCHAR(MAX), @result NVARCHAR(MAX) OUTPUT, @Timeout INT
///   AS EXTERNAL NAME SQLServerAPI.SQLServerAPI.SQLApiPost;
///
/// ⚠️ Requer PERMISSION_SET = UNSAFE para acesso à rede.
/// ⚠️ Credenciais (FTP, WebDAV) devem ser fornecidas via variáveis de ambiente,
///    nunca hardcoded no código.
/// </summary>
public class SQLServerAPI
{
    /// <summary>
    /// Realiza uma requisição HTTP POST para uma URL externa.
    /// Útil para acionar APIs REST, webhooks ou serviços externos diretamente de T-SQL.
    /// </summary>
    /// <param name="Url">URL de destino da requisição POST.</param>
    /// <param name="headers">Cabeçalhos no formato "Chave:Valor;Chave2:Valor2" (separados por ";").</param>
    /// <param name="body">Corpo da requisição em JSON.</param>
    /// <param name="result">Resposta da API ou mensagem de erro em caso de falha.</param>
    /// <param name="Timeout">Timeout em segundos. Se nulo ou zero, usa 10 segundos.</param>
    [SqlProcedure]
    public static void SQLApiPost(SqlString Url, SqlString headers, SqlString body, out SqlString result, SqlInt32 Timeout)
    {
        // 1. Timeout de segurança: Se vier nulo ou zero, usamos 5 segundos. 
        // Em SQL CLR nunca use 30s, é tempo demais para o motor do banco esperar.
        int timeoutMs = (Timeout.IsNull || Timeout.Value <= 0) ? 10000 : Timeout.Value * 1000;

        try
        {
            // 2. TLS 1.2 - Configuração simplificada
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url.ToString());
            request.Method = "POST";
            request.ContentType = "application/json";

            // 3. ATRIBUIÇÃO FIXA DO TIMEOUT (Aqui era o erro: antes só atribuía se fosse maior que zero)
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            // 4. PERFORMANCE: Desabilitar Proxy e Expect100 para ganhar velocidade no handshake
            request.Proxy = null;
            request.ServicePoint.Expect100Continue = false;

            // Configuração de Headers
            if (!headers.IsNull)
            {
                foreach (var header in headers.ToString().Split(';'))
                {
                    var parts = header.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim();
                        string val = parts[1].Trim();
                        // Headers restritos precisam ser tratados com cuidado, mas para API costuma funcionar assim:
                        if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) request.ContentType = val;
                        else request.Headers[key] = val;
                    }
                }
            }

            byte[] bodyBytes = Encoding.UTF8.GetBytes(body.IsNull ? "" : body.ToString());
            request.ContentLength = bodyBytes.Length;

            // 5. USANDO TIMEOUT NO STREAM: Importante para não travar na escrita
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                result = reader.ReadToEnd();
            }
        }
        catch (WebException webEx)
        {
            // Tratamento de erro detalhado para não deixar o SPID "pendurado"
            if (webEx.Status == WebExceptionStatus.Timeout)
            {
                result = new SqlString("Erro: Timeout de " + (timeoutMs / 1000) + "s atingido.");
            }
            else if (webEx.Response != null)
            {
                using (var errorResponse = (HttpWebResponse)webEx.Response)
                using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                {
                    result = new SqlString("Protocol Error (" + (int)errorResponse.StatusCode + "): " + reader.ReadToEnd());
                }
            }
            else
            {
                result = new SqlString("Erro de Rede: " + webEx.Status.ToString());
            }
        }
        catch (Exception ex)
        {
            result = new SqlString("Erro Geral: " + ex.Message);
        }
    }

    /// <summary>
    /// Realiza uma requisição HTTP GET para uma URL externa.
    /// Útil para consultar APIs REST, verificar status de serviços ou buscar dados externos.
    /// </summary>
    /// <param name="Url">URL da requisição GET.</param>
    /// <param name="headers">Cabeçalhos no formato "Chave:Valor;Chave2:Valor2".</param>
    /// <param name="result">Corpo da resposta ou mensagem de erro.</param>
    [SqlProcedure]
    public static void SQLApiGet(SqlString Url, SqlString headers, out SqlString result)
    {
        try
        {
            // Configura o protocolo de segurança TLS 1.2 para requisições HTTPS
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; // Garante que o TLS 1.2 está ativado

            // Cria uma requisição HTTP usando a URL fornecida
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url.ToString());
            request.Method = "GET"; // Define o método como GET
            request.ContentType = "application/json"; // Define o tipo de conteúdo como JSON

            // Configura os cabeçalhos da requisição a partir da string fornecida
            foreach (var header in headers.ToString().Split(';')) // Divide os cabeçalhos pelo delimitador ";"
            {
                var parts = header.Split(new[] { ':' }, 2); // Divide cada cabeçalho na primeira ocorrência de ":"
                if (parts.Length == 2) // Verifica se há nome e valor para o cabeçalho
                {
                    request.Headers[parts[0].Trim()] = parts[1].Trim(); // Adiciona o cabeçalho à requisição
                }
            }

            // Obtém a resposta do servidor
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream())) // Lê a resposta usando um StreamReader
            {
                string responseText = reader.ReadToEnd(); // Lê todo o conteúdo da resposta
                result = responseText; // Define o resultado como o texto da resposta
            }
        }
        catch (WebException webEx)
        {
            if (webEx.Response != null)
            {
                using (var errorResponse = (HttpWebResponse)webEx.Response)
                using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                {
                    string errorText = reader.ReadToEnd();
                    result = new SqlString(errorText);
                }
            }
            else
            {
                result = new SqlString($"Erro sem resposta: {webEx.Message}");
            }
        }
        catch (Exception ex)
        {
            result = new SqlString($"Erro: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifica se um arquivo existe em uma URL protegida via HTTP HEAD + Basic Auth.
    /// Retorna true se o servidor retornar HTTP 200, false em qualquer outro caso (404, timeout, etc.).
    /// Username e password são passados como parâmetros SQL — configure via EXEC com valores
    /// lidos de uma tabela de configuração segura, nunca hardcodados em chamadas T-SQL.
    /// </summary>
    [SqlProcedure]
    public static SqlBoolean SQLFileExists(SqlString url, SqlString username, SqlString password)
    {
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url.Value);
            request.Method = "HEAD"; // só verifica existência
            string auth = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{username.Value}:{password.Value}"));
            request.Headers.Add("Authorization", "Basic " + auth);
            request.Timeout = 5000;

            using (var response = request.GetResponse())
            {
                return true; // status 200 OK significa que existe
            }
        }
        catch (WebException ex)
        {
            if (ex.Response is HttpWebResponse resp)
            {
                if (resp.StatusCode == HttpStatusCode.NotFound) return false;
            }
            return false; // qualquer erro → assume que não existe
        }
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.Length % 2 != 0)
            throw new ArgumentException("String hexadecimal inválida");

        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }


    /// <summary>
    /// Faz upload de um arquivo via FTP.
    /// O conteúdo pode ser fornecido em Base64 (isHexaDecimal = "0") ou
    /// como string hexadecimal (isHexaDecimal = "1") — útil quando o SQL Server
    /// retorna binários como VARBINARY convertido para HEX.
    ///
    /// Credenciais FTP lidas de variáveis de ambiente: FTP_USER e FTP_PASSWORD.
    /// </summary>
    [SqlProcedure]
    public static void SQLUploadFile(SqlString url, SqlString content, SqlString isHexaDecimal, out SqlString result)
    {
        try
        {
            byte[] fileBytes;

            if (isHexaDecimal.Value == "1")
            {
                // Limpa e converte de hexadecimal para bytes
                string cleanHex = content.Value.Replace(" ", "").Replace("\n", "").Replace("\r", "");
                fileBytes = HexToBytes(cleanHex);
            }
            else
            {
                // Converte de Base64 para bytes
                fileBytes = Convert.FromBase64String(content.Value);
            }

            string fullUrl =   url.Value  ;

            var request = (FtpWebRequest)WebRequest.Create(fullUrl);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            // ⚠️ Credenciais FTP via variáveis de ambiente — nunca hardcode.
            //    Windows: setx FTP_USER "usuario" / setx FTP_PASSWORD "senha"
            string ftpUser = Environment.GetEnvironmentVariable("FTP_USER")
                ?? throw new InvalidOperationException("Variável de ambiente FTP_USER não configurada.");
            string ftpPassword = Environment.GetEnvironmentVariable("FTP_PASSWORD")
                ?? throw new InvalidOperationException("Variável de ambiente FTP_PASSWORD não configurada.");
            request.Credentials = new NetworkCredential(ftpUser, ftpPassword);
            request.UseBinary = true;
            request.UsePassive = true;
            request.KeepAlive = false;
            request.Timeout = 15000;

            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(fileBytes, 0, fileBytes.Length);
            }

            // Confirma resposta
            using (var response = (FtpWebResponse)request.GetResponse())
            {
                result = new SqlString(
                "{" +
                    "success = " + (response.StatusCode == FtpStatusCode.ClosingData).ToString() + ", " +
                    " message = \"Upload finalizado: " + response.StatusDescription.Trim() +
                " \"}"
                );
            }
        }
        catch (Exception ex)
        {
            result = new SqlString(
"{" +
    "success = false, " +
    " message = \"Erro ao enviar arquivo: " + ex.Message.Trim() +
" \"}"
);

        }

         
         
    }



    // Método auxiliar para criar a pasta via WebDAV
    private static void EnsureWebDavFolder(string urlSalvarArquivo, NetworkCredential credentials)
    {
        // Extrai a URL da pasta (removendo o nome do arquivo)
        // Ex: https://.../uploads/1/123456/arquivo.png -> https://.../uploads/1/123456/
        string folderUrl = urlSalvarArquivo.Substring(0, urlSalvarArquivo.LastIndexOf('/') + 1);

        // Divide o caminho para criar pastas uma a uma (Ex: 1, depois 123456)
        Uri uri = new Uri(folderUrl);
        string baseUrl = uri.GetLeftPart(UriPartial.Authority);
        string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        string currentPath = baseUrl + "/";

        foreach (string segment in segments)
        {
            currentPath += segment + "/";
            try
            {
                HttpWebRequest mkdirRequest = (HttpWebRequest)WebRequest.Create(currentPath);
                mkdirRequest.Method = "MKCOL"; // Comando WebDAV para criar pasta
                mkdirRequest.Credentials = credentials;
                using (HttpWebResponse res = (HttpWebResponse)mkdirRequest.GetResponse()) { }
            }
            catch (WebException ex)
            {
                // Se der erro 405 (Já existe) ou 412, apenas ignoramos e seguimos para a próxima subpasta
                if (ex.Response is HttpWebResponse resp &&
                   (resp.StatusCode == HttpStatusCode.MethodNotAllowed || resp.StatusCode == HttpStatusCode.PreconditionFailed))
                    continue;
            }
        }
    }

    /// <summary>
    /// Pipeline completo: baixa um arquivo via HTTP GET e faz upload via WebDAV (HTTP PUT).
    /// Cria automaticamente as subpastas necessárias via MKCOL antes do upload.
    ///
    /// Fluxo:
    ///   1. GET na URL de origem (ex: arquivo no servidor de mídia)
    ///   2. MKCOL recursivo para garantir que o diretório de destino existe no WebDAV
    ///   3. PUT no urlSalvarArquivo com o conteúdo baixado
    ///
    /// Credenciais WebDAV lidas de variáveis de ambiente: WEBDAV_USER e WEBDAV_PASSWORD.
    /// </summary>
    [SqlProcedure]
    public static void SQLGetFileAndUpload(
            SqlString Url,
            SqlString headers,
            SqlString mime_type,
            out SqlString result,
            SqlInt32 Timeout,
            SqlString urlSalvarArquivo)
    {
        try
        {
            int timeoutMs = (Timeout.IsNull || Timeout.Value <= 0) ? 30000 : Timeout.Value * 1000;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            // ⚠️ Credenciais WebDAV via variáveis de ambiente — nunca hardcode.
            //    Windows: setx WEBDAV_USER "usuario" / setx WEBDAV_PASSWORD "senha"
            string webDavUser = Environment.GetEnvironmentVariable("WEBDAV_USER")
                ?? throw new InvalidOperationException("Variável de ambiente WEBDAV_USER não configurada.");
            string webDavPassword = Environment.GetEnvironmentVariable("WEBDAV_PASSWORD")
                ?? throw new InvalidOperationException("Variável de ambiente WEBDAV_PASSWORD não configurada.");
            NetworkCredential creds = new NetworkCredential(webDavUser, webDavPassword);

            // --- PASSO 1: BAIXAR O ARQUIVO ---
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url.ToString());
            request.Method = "GET";
            request.Timeout = timeoutMs;

            // Adiciona headers (Token do Facebook/Meta)
            foreach (var header in headers.ToString().Split(';'))
            {
                var parts = header.Split(new[] { ':' }, 2);
                if (parts.Length == 2) request.Headers[parts[0].Trim()] = parts[1].Trim();
            }

            byte[] fileBytes;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (MemoryStream memory = new MemoryStream())
            {
                responseStream.CopyTo(memory);
                fileBytes = memory.ToArray();
            }

            // --- PASSO 2: GARANTIR PASTAS (MKCOL) ---
            EnsureWebDavFolder(urlSalvarArquivo.Value, creds);

            // --- PASSO 3: UPLOAD (PUT) ---
            HttpWebRequest putRequest = (HttpWebRequest)WebRequest.Create(urlSalvarArquivo.Value);
            putRequest.Method = "PUT";
            putRequest.Credentials = creds;
            putRequest.PreAuthenticate = true;
            putRequest.Headers.Add("Overwrite", "T"); // Força sobrescrever se houver conflito de arquivo
            putRequest.ContentLength = fileBytes.Length;
            putRequest.ContentType = mime_type.ToString();

            using (Stream putStream = putRequest.GetRequestStream())
                putStream.Write(fileBytes, 0, fileBytes.Length);

            using (HttpWebResponse putResponse = (HttpWebResponse)putRequest.GetResponse())
            {
                result = new SqlString("{\"success\": true, \"message\": \"Upload realizado com sucesso.\"}");
            }
        }
        catch (Exception ex)
        {
            result = new SqlString("{\"success\": false, \"message\": \"" + ex.Message.Replace("\"", "'") + "\"}");
        }
    }

    /// <summary>
    /// Baixa um arquivo via HTTP GET (ex: de um servidor de armazenamento interno)
    /// e envia como multipart/form-data via HTTP POST para uma API externa (ex: API Graph do Facebook/Meta).
    ///
    /// Fluxo:
    ///   1. GET em urlBuscaArquivo para baixar o arquivo binário
    ///   2. Monta um corpo multipart/form-data com o arquivo e o campo "filename"
    ///   3. POST na Url de destino com os headers fornecidos (incluindo token de autorização)
    ///
    /// A variável `passos` rastreia em qual etapa um erro ocorreu — útil para debug
    /// de falhas em ambientes de produção onde não é possível depurar com breakpoints.
    /// </summary>
    [SqlProcedure]
    public static void SQLUploadFileFacebook(
            SqlString Url,
            SqlString headers,
            SqlString filename,
            out SqlString result,
            SqlInt32 Timeout,
            SqlString urlBuscaArquivo)
    {
        try
        {
            int timeoutMs = (Timeout.IsNull || Timeout.Value <= 0)
                ? 30000
                : Timeout.Value * 1000;

            string passos = "1";

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // Cria requisição HTTP para gerar o áudio (OpenAI)
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(urlBuscaArquivo.ToString());
            request.Method = "GET";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            passos = passos+" - 2";
            // GET não envia body
            request.ContentLength = 0;

            // Lê resposta binária (arquivo)
            byte[] fileBytes;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (MemoryStream memory = new MemoryStream())
            {
                responseStream.CopyTo(memory);
                fileBytes = memory.ToArray();
            }
            passos = passos + " - 3";
            // Envia via WebDAV (HTTP PUT)
            try
            {
                // Se filename não informado, tenta extrair do parâmetro urlBuscaArquivo
                string fileNameValue = filename.IsNull || filename.Value.Trim() == ""
                    ? Path.GetFileName(new Uri(urlBuscaArquivo.Value).LocalPath)
                    : filename.Value;

                if (string.IsNullOrEmpty(fileNameValue))
                    fileNameValue = "file.bin";
                passos = passos + " - 4";
                // 2) Monta multipart/form-data
                string boundary = "----Boundary" + Guid.NewGuid().ToString("N");
                byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary + "\r\n");
                byte[] endBoundaryBytes = Encoding.UTF8.GetBytes("--" + boundary + "--\r\n");
                byte[] crlf = Encoding.UTF8.GetBytes("\r\n");
                passos = passos + " - 5";
                using (var bodyStream = new MemoryStream())
                {
                    // --boundary
                    bodyStream.Write(boundaryBytes, 0, boundaryBytes.Length);

                    // Content-Disposition for file field
                    string headerFile = $"Content-Disposition: form-data; name=\"file\"; filename=\"{fileNameValue}\"\r\n" +
                                        "Content-Type: application/octet-stream\r\n\r\n";
                    var headerFileBytes = Encoding.UTF8.GetBytes(headerFile);
                    bodyStream.Write(headerFileBytes, 0, headerFileBytes.Length);
                    passos = passos + " - 6";
                    // file bytes
                    bodyStream.Write(fileBytes, 0, fileBytes.Length);
                    bodyStream.Write(crlf, 0, crlf.Length);

                    // --boundary for filename field
                    bodyStream.Write(boundaryBytes, 0, boundaryBytes.Length);
                    passos = passos + " - 7";
                    // Content-Disposition for filename (text field)
                    string headerFilename = $"Content-Disposition: form-data; name=\"filename\"\r\n\r\n";
                    var headerFilenameBytes = Encoding.UTF8.GetBytes(headerFilename);
                    bodyStream.Write(headerFilenameBytes, 0, headerFilenameBytes.Length);
                    passos = passos + " - 8";
                    var filenameValueBytes = Encoding.UTF8.GetBytes(fileNameValue + "\r\n");
                    bodyStream.Write(filenameValueBytes, 0, filenameValueBytes.Length);

                    // final boundary
                    bodyStream.Write(endBoundaryBytes, 0, endBoundaryBytes.Length);

                    bodyStream.Flush();
                    passos = passos + " - 9";
                    // 3) POST multipart to Url
                    var postRequest = (HttpWebRequest)WebRequest.Create(Url.Value);
                    postRequest.Method = "POST";
                    postRequest.Timeout = timeoutMs;
                    postRequest.ReadWriteTimeout = postRequest.Timeout;
                    postRequest.ServicePoint.Expect100Continue = false;
                    postRequest.KeepAlive = true; 
                    postRequest.Proxy = null; 
                    postRequest.ProtocolVersion = HttpVersion.Version11;
                    passos = passos + " - 10";
                    postRequest.ContentLength = bodyStream.Length; // <-- Adicionar este
                    postRequest.UserAgent = "SQLCLRUploader/1.0";



                    postRequest.ContentType = "multipart/form-data; boundary=" + boundary; 
                    passos = passos + " - 11";
                    // Headers (except Content-Type/Content-Length which we've set)
                    if (!headers.IsNull && headers.Value.Length > 0)
                    {
                        foreach (var header in headers.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = header.Split(new[] { ':' }, 2);
                            if (parts.Length == 2)
                            {
                                string name = parts[0].Trim();
                                string value = parts[1].Trim();

                                // Alguns cabeçalhos são restritos e devem ser setados nas propriedades apropriadas.
                                // Authorization é permitido via Headers.
                                if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                                {
                                    // ignore, já definidos
                                }
                                else
                                {
                                    postRequest.Headers[name] = value;
                                }
                            }
                        }
                    }
                    passos = passos + " - 12";
                    // Write body
                    using (var reqStream = postRequest.GetRequestStream())
                    {
                        bodyStream.Position = 0;
                        bodyStream.CopyTo(reqStream);
                    }
                    passos = passos + " - 13";
                    // 4) Read response
                    using (var response = (HttpWebResponse)postRequest.GetResponse())
                    using (var respStream = response.GetResponseStream())
                    using (var reader = new StreamReader(respStream, Encoding.UTF8))
                    {
                        passos = passos + " - 14";
                        string responseText = reader.ReadToEnd();
                        result = new SqlString(responseText);
                        passos = passos + " - 15";
                    }
                }
            }
            catch (WebException ex)
            {
                string msg = ex.Message;
                if (ex.Response != null)
                {
                    try
                    {
                        using (var s = ex.Response.GetResponseStream())
                        using (var r = new StreamReader(s))
                            msg = r.ReadToEnd();
                    }
                    catch { }
                }

                result = new SqlString(
                    "{\"success\": false, \"message\": \"Erro ao enviar via WebDAV: "+ passos+" - " +
                    msg.Replace("\"", "'") + "\"}"
                );
            }
        }
        catch (WebException webEx)
        {
            string msg = webEx.Message;
            if (webEx.Response != null)
            {
                try
                {
                    using (var stream = webEx.Response.GetResponseStream())
                    using (var reader = new StreamReader(stream))
                        msg = reader.ReadToEnd();
                }
                catch { }
            }
            result = new SqlString("{\"success\": false, \"message\": \"" + msg.Replace("\"", "'") + "\"}");
        }
        catch (Exception ex)
        {
            result = new SqlString("{\"success\": false, \"message\": \"" + ex.Message.Replace("\"", "'") + "\"}");
        }
    }
}
