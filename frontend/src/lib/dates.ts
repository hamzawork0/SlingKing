const ist = new Intl.DateTimeFormat("en-IN", {
  timeZone: "Asia/Kolkata",
  day: "2-digit",
  month: "short",
  year: "numeric",
});

const istWithTime = new Intl.DateTimeFormat("en-IN", {
  timeZone: "Asia/Kolkata",
  day: "2-digit",
  month: "short",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

/** Timestamps are stored UTC and always rendered in IST. */
export function formatIstDate(iso: string): string {
  return ist.format(new Date(iso));
}

export function formatIstDateTime(iso: string): string {
  return istWithTime.format(new Date(iso));
}
