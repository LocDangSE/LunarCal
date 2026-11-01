"""Parse HKO Gregorian-Lunar conversion tables (T1901e.txt..T2100e.txt) into CSV fixtures.

Usage: python parse_hko.py <hko_dir> <out_dir>
Outputs:
  hko-month-starts.csv : solar_date,lunar_year,lunar_month,is_leap   (first day of every lunar month)
  hko-solar-terms.csv  : solar_date,term                              (day on which a solar term starts)
"""
import collections
import re
import sys
from pathlib import Path

ROW = re.compile(
    r"^(\d{4})/(\d{1,2})/(\d{1,2})\s+(\S.*?)\s{2,}"
    r"(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\s*(.*?)\s*$"
)
MONTH = re.compile(r"^(\d{1,2})(?:st|nd|rd|th) Lunar Month$", re.IGNORECASE)

# HKO English term names -> SolarTerm enum member names (LunarCal.Core.Lunar.SolarTerm).
TERMS = {
    "Vernal Equinox": "XuanPhan", "Bright & Clear": "ThanhMinh", "Corn Rain": "CocVu",
    "Summer Commences": "LapHa", "Corn Forms": "TieuMan", "Corn on Ear": "MangChung",
    "Summer Solstice": "HaChi", "Moderate Heat": "TieuThu", "Great Heat": "DaiThu",
    "Autumn Commences": "LapThu", "End of Heat": "XuThu", "White Dew": "BachLo",
    "Autumnal Equinox": "ThuPhan", "Cold Dew": "HanLo", "Frost": "SuongGiang",
    "Winter Commences": "LapDong", "Light Snow": "TieuTuyet", "Heavy Snow": "DaiTuyet",
    "Winter Solstice": "DongChi", "Moderate Cold": "TieuHan", "Severe Cold": "DaiHan",
    "Spring Commences": "LapXuan", "Spring Showers": "VuThuy", "Insects Waken": "KinhTrap",
}


def main(src: Path, out: Path) -> None:
    rows = []
    for year in range(1901, 2101):
        text = (src / f"T{year}e.txt").read_bytes().decode("latin-1")
        for line in text.splitlines():
            m = ROW.match(line)
            if m:
                y, mo, d, lunar, _, term = m.groups()
                rows.append((f"{int(y):04d}-{int(mo):02d}-{int(d):02d}", lunar.strip(), term.strip()))
    assert len(rows) == 73048, len(rows)  # 1901-01-01..2100-12-31 minus 2069-12-30 (missing in the HKO source)

    unknown = collections.Counter(t for _, _, t in rows if t and t not in TERMS)
    assert not unknown, unknown

    months, terms = [], []
    prev_month = None
    lunar_year = 1900  # 1901-01-01 is in lunar year 1900 (month 11)
    for date, lunar, term in rows:
        if term:
            terms.append((date, TERMS[term]))
        mm = MONTH.match(lunar)
        if mm:
            n = int(mm.group(1))
            leap = n == prev_month
            if n == 1 and not leap:
                lunar_year = int(date[:4])
            months.append((date, lunar_year, n, leap))
            prev_month = n
        else:
            assert lunar.isdigit() and 2 <= int(lunar) <= 30, (date, lunar)

    with open(out / "hko-month-starts.csv", "w", encoding="utf-8", newline="\r\n") as f:
        f.write("# Source: Hong Kong Observatory, Gregorian-Lunar Calendar Conversion Table 1901-2100 (UTC+8). See README.md.\n")
        f.write("solar_date,lunar_year,lunar_month,is_leap\n")
        for date, y, n, leap in months:
            f.write(f"{date},{y},{n},{'true' if leap else 'false'}\n")
    with open(out / "hko-solar-terms.csv", "w", encoding="utf-8", newline="\r\n") as f:
        f.write("# Source: Hong Kong Observatory, Gregorian-Lunar Calendar Conversion Table 1901-2100 (UTC+8). See README.md.\n")
        f.write("solar_date,term\n")
        for date, t in terms:
            f.write(f"{date},{t}\n")
    print(f"days={len(rows)} month_starts={len(months)} leap={sum(1 for m in months if m[3])} terms={len(terms)}")


if __name__ == "__main__":
    main(Path(sys.argv[1]), Path(sys.argv[2]))
