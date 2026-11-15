using System.Text.Json;

namespace DeliveryQuote;

public sealed class ProductDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Category { get; set; }
    public long? UnitPriceCents { get; set; }
    public int? Stock { get; set; }
}

public static class DataStore
{
    public const string RequestsHeader = "requestId,productId,quantity,distanceKm";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static Result<Catalog> LoadCatalog(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var dtos = JsonSerializer.Deserialize<List<ProductDto>>(stream, JsonOptions);
            if (dtos is null)
                return Result.Fail<Catalog>("MALFORMED_DATA", $"File '{path}' contains JSON null instead of a product list.");

            var products = new List<Product>();
            for (var i = 0; i < dtos.Count; i++)
            {
                var created = ToProduct(dtos[i], i + 1);
                switch (created)
                {
                    case Result<Product>.Success s:
                        products.Add(s.Value);
                        break;
                    case Result<Product>.Failure f:
                        return Result.Fail<Catalog>(f.Error.Code, $"{path}: {f.Error.Message}");
                }
            }
            return Catalog.Create(products);
        }
        catch (FileNotFoundException)
        {
            return Result.Fail<Catalog>("FILE_NOT_FOUND", $"Data file not found: {path}");
        }
        catch (DirectoryNotFoundException)
        {
            return Result.Fail<Catalog>("FILE_NOT_FOUND", $"Directory of data file not found: {path}");
        }
        catch (JsonException ex)
        {
            return Result.Fail<Catalog>("MALFORMED_DATA", $"File '{path}' is not valid product JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail<Catalog>("FILE_UNREADABLE", $"Cannot read '{path}': {ex.Message}");
        }
    }

    private static Result<Product> ToProduct(ProductDto? dto, int position)
    {
        if (dto is null)
            return Result.Fail<Product>("MALFORMED_DATA", $"Product entry #{position} is null.");
        if (dto.UnitPriceCents is null)
            return Result.Fail<Product>("MISSING_FIELD", $"Product entry #{position} is missing 'unitPriceCents'.");
        if (dto.Stock is null)
            return Result.Fail<Product>("MISSING_FIELD", $"Product entry #{position} is missing 'stock'.");

        return Product.Create(dto.Id, dto.Name, dto.Category, dto.UnitPriceCents.Value, dto.Stock.Value);
    }

    public static IEnumerable<RawRequest> StreamRequests(string path)
    {
        using var reader = new StreamReader(path);

        var header = reader.ReadLine();
        if (header is null)
            throw new FormatException($"'{path}' is empty; expected header '{RequestsHeader}'.");
        if (header.Trim() != RequestsHeader)
            throw new FormatException($"'{path}' has header '{header.Trim()}'; expected '{RequestsHeader}'.");

        var lineNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length != 4)
                throw new FormatException($"'{path}' line {lineNumber}: expected 4 columns but found {parts.Length}.");

            yield return new RawRequest(parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim());
        }
    }

    public static Result<IReadOnlyList<RawRequest>> LoadRequests(string path)
    {
        try
        {
            IReadOnlyList<RawRequest> snapshot = StreamRequests(path).ToArray();
            return Result.Ok(snapshot);
        }
        catch (FileNotFoundException)
        {
            return Result.Fail<IReadOnlyList<RawRequest>>("FILE_NOT_FOUND", $"Data file not found: {path}");
        }
        catch (DirectoryNotFoundException)
        {
            return Result.Fail<IReadOnlyList<RawRequest>>("FILE_NOT_FOUND", $"Directory of data file not found: {path}");
        }
        catch (FormatException ex)
        {
            return Result.Fail<IReadOnlyList<RawRequest>>("MALFORMED_DATA", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail<IReadOnlyList<RawRequest>>("FILE_UNREADABLE", $"Cannot read '{path}': {ex.Message}");
        }
    }

    public static Result<string> SaveCatalog(string path, Catalog catalog)
    {
        var temp = path + ".tmp";
        try
        {
            EnsureDirectory(path);
            var dtos = catalog.Products.Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Category = p.Category,
                UnitPriceCents = p.UnitPriceCents,
                Stock = p.Stock
            }).ToList();

            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, dtos, JsonOptions);
            }
            File.Move(temp, path, overwrite: true);
            return Result.Ok(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return Result.Fail<string>("FILE_WRITE_FAILED", $"Cannot save '{path}': {ex.Message}");
        }
    }

    public static Result<string> ExportLines(string path, IEnumerable<string> lines)
    {
        try
        {
            EnsureDirectory(path);
            File.WriteAllLines(path, lines);
            return Result.Ok(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail<string>("FILE_WRITE_FAILED", $"Cannot export '{path}': {ex.Message}");
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
