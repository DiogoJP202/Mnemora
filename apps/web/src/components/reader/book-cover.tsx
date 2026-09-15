export function BookCover({ title, coverUrl, large = false }: { title: string; coverUrl?: string | null; large?: boolean }) {
  let safeUrl: string | null = null;
  try {
    if (coverUrl) {
      const url = new URL(coverUrl);
      if (url.protocol === "http:" || url.protocol === "https:") {
        url.protocol = "https:";
        safeUrl = url.toString();
      }
    }
  } catch { /* Use the illustrated cover for invalid URLs. */ }
  return <div className={`book-cover ${large ? "book-cover-large" : ""}`} style={safeUrl ? { backgroundImage: `url(${JSON.stringify(safeUrl)})` } : undefined} role="img" aria-label={safeUrl ? `Capa de ${title}` : `Capa ilustrativa de ${title}`}>{!safeUrl && <><span className="cover-line" /><span className="cover-title">{title}</span><span className="cover-symbol">✳</span></>}</div>;
}
