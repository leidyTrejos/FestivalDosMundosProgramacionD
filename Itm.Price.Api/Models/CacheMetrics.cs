namespace Itm.Price.Api.Models;

public class CacheMetrics
{
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public int TotalRequests => CacheHits + CacheMisses;
    public double HitRate => TotalRequests > 0 ? Math.Round((double)CacheHits / TotalRequests * 100, 2) : 0;

    public void RecordHit() => CacheHits++;
    public void RecordMiss() => CacheMisses++;
}