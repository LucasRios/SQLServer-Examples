using Microsoft.SqlServer.Server;
using System;
using System.Data;
using System.Data.SqlTypes;
using System.IO;
using System.Net;
using System.Security.Policy;
using System.Text;

/// <summary>
/// Classe que define um procedimento armazenado SQL CLR (Common Language Runtime)
/// para fazer upload de um arquivo de áudio e obter sua transcrição via API da OpenAI (Whisper).
/// </summary>
public class OpenAITraduzirAudio
{
    /// <summary>
    /// Procedimento SQL que faz o upload de um arquivo de áudio e retorna o texto transcrito.
    /// Pode receber um caminho local ou uma URL.
    /// </summary>
    /// <param name="filePath">Caminho local do arquivo de áudio ou uma URL para download</param>
    /// <param name="result">Texto retornado da transcrição (ou mensagem de erro)</param>
    [SqlProcedure]
    public static void UploadAudioAndReturnText(SqlString filePath, out SqlString result)
    {
        // URL da API de transcrição de áudio da OpenAI
        string apiUrl = "https://api.openai.com/v1/audio/transcriptions";

        // Token de acesso à API da OpenAI.
        // ⚠️ NUNCA inclua chaves reais no código. Configure via variável de ambiente do SO:
        //    Windows: setx OPENAI_API_KEY "sk-..."
        //    Linux/macOS: export OPENAI_API_KEY="sk-..."
        // Ao registrar a assembly no SQL Server, o processo sqlservr.exe precisa ter essa
        // variável definida no ambiente do serviço.
        string accessToken = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("Variável de ambiente OPENAI_API_KEY não configurada no servidor SQL.");

        // Variável que armazenará o caminho local do arquivo (mesmo que venha de uma URL)
        string localFilePath = "";

        try
        {
            localFilePath = filePath.Value;

            // Se for uma URL (começa com http), faz o download para um arquivo temporário
            if (filePath.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                localFilePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(filePath.Value));
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(filePath.Value, localFilePath);
                }
            }

            // Garante que o protocolo TLS 1.2 será usado (necessário para segurança nas requisições HTTPS)
            System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            // Gera um boundary único para separação de partes no corpo da requisição multipart/form-data
            string boundary = "------------------------" + DateTime.Now.Ticks.ToString("x");

            try
            {
                // Cria uma requisição HTTP POST
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(apiUrl);
                request.Method = "POST";
                request.ContentType = "multipart/form-data; boundary=" + boundary;
                request.Headers["Authorization"] = "Bearer " + accessToken;

                // Prepara o corpo da requisição
                using (Stream requestStream = request.GetRequestStream())
                using (StreamWriter writer = new StreamWriter(requestStream))
                {
                    // Adiciona o arquivo ao corpo da requisição
                    writer.WriteLine("--" + boundary);
                    writer.WriteLine($"Content-Disposition: form-data; name=\"file\"; filename=\"{Path.GetFileName(localFilePath)}\"");
                    writer.WriteLine("Content-Type: audio/ogg");
                    writer.WriteLine();
                    writer.Flush(); // Escreve cabeçalhos antes do conteúdo do arquivo

                    // Copia o conteúdo binário do arquivo diretamente para o stream
                    using (FileStream fileStream = File.OpenRead(localFilePath))
                    {
                        fileStream.CopyTo(requestStream);
                    }

                    writer.WriteLine();

                    // Adiciona o modelo Whisper-1
                    writer.WriteLine("--" + boundary);
                    writer.WriteLine("Content-Disposition: form-data; name=\"model\"");
                    writer.WriteLine();
                    writer.WriteLine("gpt-4o-mini-transcribe");

                    // Define o idioma como português
                    writer.WriteLine("--" + boundary);
                    writer.WriteLine("Content-Disposition: form-data; name=\"language\"");
                    writer.WriteLine();
                    writer.WriteLine("pt");

                    // Finaliza o corpo da requisição
                    writer.WriteLine("--" + boundary + "--");
                    writer.Flush();
                }

                // Envia a requisição e obtém a resposta
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    string responseText = reader.ReadToEnd();

                    // Limpa o arquivo temporário baixado, se existir
                    if (localFilePath != "" && File.Exists(localFilePath))
                    {
                        File.Delete(localFilePath);
                    }

                    // Retorna a resposta como output do procedimento
                    result = responseText;
                }
            }
            catch (Exception ex)
            {
                // Trata falhas da requisição e limpa o arquivo temporário
                if (localFilePath != "" && File.Exists(localFilePath))
                {
                    File.Delete(localFilePath);
                }

                throw new Exception($"Erro: {ex.Message}");
            }
            finally
            {
                // Garante que o arquivo local temporário seja removido mesmo em sucesso
                if (localFilePath != "" && File.Exists(localFilePath))
                {
                    File.Delete(localFilePath);
                }
            }

        }
        catch (Exception ex)
        {
            // Em caso de erro geral (como falha no download ou leitura), retorna a mensagem de erro
            if (localFilePath != "" && File.Exists(localFilePath))
            {
                File.Delete(localFilePath);
            }

            result = new SqlString($"Erro: {ex.Message}");
        }
    }

    [SqlProcedure]
    public static void UploadTextAndReturnAudio(
            SqlString Url,
            SqlString headers,
            SqlString body,
            out SqlString result,
            SqlInt32 Timeout,
            SqlString urlSalvarArquivo)
    {
        try
        {
            int timeoutMs = (Timeout.IsNull || Timeout.Value <= 0)
                ? 30000
                : Timeout.Value * 1000;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // Cria requisição HTTP para gerar o áudio (OpenAI)
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url.ToString());
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            // Adiciona cabeçalhos
            foreach (var header in headers.ToString().Split(';'))
            {
                var parts = header.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                    request.Headers[parts[0].Trim()] = parts[1].Trim();
            }

            // Escreve corpo JSON
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body.ToString());
            request.ContentLength = bodyBytes.Length;
            using (Stream requestStream = request.GetRequestStream())
                requestStream.Write(bodyBytes, 0, bodyBytes.Length);

            // Lê resposta binária (áudio)
            byte[] fileBytes;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (MemoryStream memory = new MemoryStream())
            {
                responseStream.CopyTo(memory);
                fileBytes = memory.ToArray();
            }

            // Envia via WebDAV (HTTP PUT)
            try
            {
                string fullUrl = urlSalvarArquivo.Value;  

                HttpWebRequest putRequest = (HttpWebRequest)WebRequest.Create(fullUrl);
                putRequest.Method = "PUT";
                // ⚠️ Credenciais WebDAV via variáveis de ambiente — nunca hardcode em código público.
                //    Windows: setx WEBDAV_USER "usuario" / setx WEBDAV_PASSWORD "senha"
                string webDavUser = Environment.GetEnvironmentVariable("WEBDAV_USER")
                    ?? throw new InvalidOperationException("Variável de ambiente WEBDAV_USER não configurada.");
                string webDavPassword = Environment.GetEnvironmentVariable("WEBDAV_PASSWORD")
                    ?? throw new InvalidOperationException("Variável de ambiente WEBDAV_PASSWORD não configurada.");
                putRequest.Credentials = new NetworkCredential(webDavUser, webDavPassword);
                putRequest.Timeout = 15000;
                putRequest.ReadWriteTimeout = 15000;
                putRequest.AllowWriteStreamBuffering = true;
                putRequest.ContentLength = fileBytes.Length;
                putRequest.ContentType = "audio/mpeg"; // tipo MIME do MP3

                using (Stream putStream = putRequest.GetRequestStream())
                    putStream.Write(fileBytes, 0, fileBytes.Length);

                using (HttpWebResponse putResponse = (HttpWebResponse)putRequest.GetResponse())
                {
                    result = new SqlString(
                        "{" +
                        "\"success\": true, " +
                        "\"status\": " + (int)putResponse.StatusCode + ", " +
                        "\"message\": \"Upload via WebDAV finalizado: " + putResponse.StatusDescription.Trim() + "\"" +
                        "}"
                    );
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
                    "{\"success\": false, \"message\": \"Erro ao enviar via WebDAV: " +
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



