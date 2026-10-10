param(
	[int]$Port = 8766,
	[string]$Root = $PSScriptRoot
)

# A throwaway static file server so the fixture page runs as a real web page (a file: tab cannot be scripted). GET serves the directory it lives in; POST /save/<name>
# writes the request body to <name> in that directory, which is how the page hands its measured rects back without the numbers ever passing through a chat transcript.
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "serving $Root on http://localhost:$Port/"

while ($listener.IsListening) {
	$context = $listener.GetContext()
	$request = $context.Request
	$response = $context.Response
	$relative = $request.Url.AbsolutePath.TrimStart('/')

	if ($request.HttpMethod -eq 'POST' -and $relative.StartsWith('save/')) {
		$name = [System.IO.Path]::GetFileName($relative.Substring(5))
		$reader = New-Object System.IO.StreamReader($request.InputStream, $request.ContentEncoding)
		$body = $reader.ReadToEnd()
		[System.IO.File]::WriteAllText((Join-Path $Root $name), $body, [System.Text.UTF8Encoding]::new($false))
		$bytes = [System.Text.Encoding]::UTF8.GetBytes("saved $name ($($body.Length) chars)")
		$response.ContentType = 'text/plain'
		$response.ContentLength64 = $bytes.Length
		$response.OutputStream.Write($bytes, 0, $bytes.Length)
		$response.Close()
		continue
	}

	if ($relative -eq '') { $relative = 'browser-cases.html' }
	$path = Join-Path $Root $relative
	if (Test-Path $path -PathType Leaf) {
		$bytes = [System.IO.File]::ReadAllBytes($path)
		if ($path.EndsWith('.html')) { $response.ContentType = 'text/html; charset=utf-8' }
		elseif ($path.EndsWith('.json')) { $response.ContentType = 'application/json' }
		else { $response.ContentType = 'application/octet-stream' }
		$response.ContentLength64 = $bytes.Length
		$response.OutputStream.Write($bytes, 0, $bytes.Length)
	}
	else {
		$response.StatusCode = 404
	}
	$response.Close()
}
