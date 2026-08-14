/* ==========================================================================
 * case-cdxtime.cpp -- CDXTIME.DBF + CDXTIME.CDX
 *
 * The datetime key case, and the reason it exists is one table.
 *
 * A 'T' key is not arithmetic. t4dateTimeToFox rounds the milliseconds to the
 * nearest second, forms day + seconds/86400, and then consults an 86400-bit
 * bitmap indexed by second-of-day; where the bit is set it decrements the
 * double by one byte, with borrow (i4conv.c:2209-2287). The bitmap is
 * empirical -- the comment at i4conv.c:1506-1509 says FoxPro's conversion
 * "could not be deciphered" -- so a port can only copy it, and copying 10800
 * bytes is worth nothing unless something can prove the copy right.
 *
 * That is what these records are for. 256 datetimes, chosen so the stored keys
 * exercise the parts a random spread would miss:
 *
 *   - both sides of the bitmap. 26.1% of seconds carry the decrement flag, and
 *     the values below are picked so roughly a third do, alternating with
 *     seconds that do not, spread across the whole day rather than clustered
 *   - the day's own edges: 00:00:00, 00:00:01, 23:59:58, 23:59:59, and the
 *     minute and hour boundaries between
 *   - calendar edges: 1900-02-28 (not a leap year), 2000-02-29 (leap, because
 *     divisible by 400), 2100-02-28 (not leap), ordinary leap days, the first
 *     and last day of months of 28, 30 and 31 days, and year rollovers
 *   - a blank datetime, which keys as day zero
 *   - two equal datetimes, so the tag holds a duplicate run
 *
 * Whole seconds only: f4assignDateTime ignores a millisecond part for 'T'
 * (F4FIELD.C:2005-2060), which is FoxPro's own behaviour. The rounding half of
 * the algorithm is plain arithmetic and is unit-tested in the port; what needs
 * a real file is the bitmap, and whole seconds exercise every bit of it.
 *
 * The data below belongs to this case alone. See cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "dump-index.h"
#include "cases.h"

#define ROWS 256

/* "YYYYMMDDHH:MM:SS", or "" for a blank datetime. */
static const char *const STAMPS[ROWS] =
{
   "1900010100:00:00",   /* first representable-ish year start */
   "1900022800:00:01",   /* 1900 is not a leap year */
   "1900030100:00:04",   /* day after 1900-02-28 */
   "1999123100:00:05",   /* last day of the century */
   "2000010100:00:59",   /* century rollover */
   "2000022900:01:00",   /* 2000 IS a leap year (div 400) */
   "2000030100:01:01",   /* day after the leap day */
   "2004022900:59:59",   /* ordinary leap year */
   "2024022901:00:00",   /* recent leap day */
   "2100022801:00:01",   /* 2100 is not a leap year */
   "2026010111:59:59",   /* year start */
   "2026123112:00:00",   /* year end */
   "2026013123:58:57",   /* month end, 31 */
   "2026020123:58:58",   /* month start after 31 */
   "2026022823:59:58",   /* month end, 28 */
   "2026030123:59:59",   /* month start after 28 */
   "2026043000:00:00",   /* month end, 30 */
   "2026050100:00:01",   /* month start after 30 */
   "2026063000:00:04",   /* half year */
   "2026070100:00:05",   /* half year plus one */
   "1970010100:00:59",   /* unix epoch */
   "1980123100:01:00",   /* a leap year's last day */
   "2026061500:00:00",   /* time edge 00:00:00 */
   "2026061500:00:01",   /* time edge 00:00:01 */
   "2026061500:00:04",   /* time edge 00:00:04 */
   "2026061500:00:05",   /* time edge 00:00:05 */
   "2026061500:00:59",   /* time edge 00:00:59 */
   "2026061500:01:00",   /* time edge 00:01:00 */
   "2026061500:01:01",   /* time edge 00:01:01 */
   "2026061500:59:59",   /* time edge 00:59:59 */
   "2026061501:00:00",   /* time edge 01:00:00 */
   "2026061501:00:01",   /* time edge 01:00:01 */
   "2026061511:59:59",   /* time edge 11:59:59 */
   "2026061512:00:00",   /* time edge 12:00:00 */
   "2026061523:58:57",   /* time edge 23:58:57 */
   "2026061523:58:58",   /* time edge 23:58:58 */
   "2026061523:59:58",   /* time edge 23:59:58 */
   "2026061523:59:59",   /* time edge 23:59:59 */
   "2026061600:00:04",   /* flag set */
   "2026061700:00:00",   /* flag clear */
   "2026061600:23:48",   /* flag set */
   "2026061700:08:25",   /* flag clear */
   "2026061600:47:26",   /* flag set */
   "2026061700:16:50",   /* flag clear */
   "2026061601:11:10",   /* flag set */
   "2026061700:25:13",   /* flag clear */
   "2026061601:34:53",   /* flag set */
   "2026061700:33:38",   /* flag clear */
   "2026061601:58:31",   /* flag set */
   "2026061700:42:03",   /* flag clear */
   "2026061602:22:15",   /* flag set */
   "2026061700:50:28",   /* flag clear */
   "2026061602:46:00",   /* flag set */
   "2026061700:58:51",   /* flag clear */
   "2026061603:09:44",   /* flag set */
   "2026061701:07:16",   /* flag clear */
   "2026061603:33:28",   /* flag set */
   "2026061701:15:41",   /* flag clear */
   "2026061603:57:11",   /* flag set */
   "2026061701:24:06",   /* flag clear */
   "2026061604:20:55",   /* flag set */
   "2026061701:32:30",   /* flag clear */
   "2026061604:44:39",   /* flag set */
   "2026061701:40:54",   /* flag clear */
   "2026061605:08:17",   /* flag set */
   "2026061701:49:19",   /* flag clear */
   "2026061605:32:00",   /* flag set */
   "2026061701:57:44",   /* flag clear */
   "2026061605:55:45",   /* flag set */
   "2026061702:06:08",   /* flag clear */
   "2026061606:19:29",   /* flag set */
   "2026061702:14:33",   /* flag clear */
   "2026061606:43:13",   /* flag set */
   "2026061702:22:57",   /* flag clear */
   "2026061607:06:51",   /* flag set */
   "2026061702:31:22",   /* flag clear */
   "2026061607:30:35",   /* flag set */
   "2026061702:39:46",   /* flag clear */
   "2026061607:54:19",   /* flag set */
   "2026061702:48:11",   /* flag clear */
   "2026061608:18:03",   /* flag set */
   "2026061702:56:36",   /* flag clear */
   "2026061608:41:46",   /* flag set */
   "2026061703:05:00",   /* flag clear */
   "2026061609:05:24",   /* flag set */
   "2026061703:13:24",   /* flag clear */
   "2026061609:29:08",   /* flag set */
   "2026061703:21:49",   /* flag clear */
   "2026061609:52:52",   /* flag set */
   "2026061703:30:16",   /* flag clear */
   "2026061610:16:36",   /* flag set */
   "2026061703:38:40",   /* flag clear */
   "2026061610:40:20",   /* flag set */
   "2026061703:47:02",   /* flag clear */
   "2026061611:04:04",   /* flag set */
   "2026061703:55:29",   /* flag clear */
   "2026061611:27:48",   /* flag set */
   "2026061704:03:54",   /* flag clear */
   "2026061611:51:32",   /* flag set */
   "2026061704:12:18",   /* flag clear */
   "2026061612:15:10",   /* flag set */
   "2026061704:20:42",   /* flag clear */
   "2026061612:38:53",   /* flag set */
   "2026061704:29:07",   /* flag clear */
   "2026061613:02:31",   /* flag set */
   "2026061704:37:32",   /* flag clear */
   "2026061613:26:15",   /* flag set */
   "2026061704:45:56",   /* flag clear */
   "2026061613:49:59",   /* flag set */
   "2026061704:54:20",   /* flag clear */
   "2026061614:13:44",   /* flag set */
   "2026061705:02:45",   /* flag clear */
   "2026061614:37:28",   /* flag set */
   "2026061705:11:10",   /* flag clear */
   "2026061615:01:12",   /* flag set */
   "2026061705:19:34",   /* flag clear */
   "2026061615:24:55",   /* flag set */
   "2026061705:27:58",   /* flag clear */
   "2026061615:48:39",   /* flag set */
   "2026061705:36:23",   /* flag clear */
   "2026061616:12:17",   /* flag set */
   "2026061705:44:48",   /* flag clear */
   "2026061616:36:00",   /* flag set */
   "2026061705:53:12",   /* flag clear */
   "2026061616:59:44",   /* flag set */
   "2026061706:01:36",   /* flag clear */
   "2026061617:23:29",   /* flag set */
   "2026061706:10:01",   /* flag clear */
   "2026061617:47:13",   /* flag set */
   "2026061706:18:26",   /* flag clear */
   "2026061618:10:57",   /* flag set */
   "2026061706:26:50",   /* flag clear */
   "2026061618:34:35",   /* flag set */
   "2026061706:35:14",   /* flag clear */
   "2026061618:58:19",   /* flag set */
   "2026061706:43:39",   /* flag clear */
   "2026061619:22:03",   /* flag set */
   "2026061706:52:04",   /* flag clear */
   "2026061619:45:46",   /* flag set */
   "2026061707:00:28",   /* flag clear */
   "2026061620:09:24",   /* flag set */
   "2026061707:08:52",   /* flag clear */
   "2026061620:33:08",   /* flag set */
   "2026061707:17:17",   /* flag clear */
   "2026061620:56:52",   /* flag set */
   "2026061707:25:42",   /* flag clear */
   "2026061621:20:36",   /* flag set */
   "2026061707:34:06",   /* flag clear */
   "2026061621:44:20",   /* flag set */
   "2026061707:42:30",   /* flag clear */
   "2026061622:08:04",   /* flag set */
   "2026061707:50:55",   /* flag clear */
   "2026061622:31:48",   /* flag set */
   "2026061707:59:20",   /* flag clear */
   "2026061622:55:32",   /* flag set */
   "2026061708:07:44",   /* flag clear */
   "2026061623:19:10",   /* flag set */
   "2026061708:16:08",   /* flag clear */
   "1980110317:31:34",   /* spread */
   "1987041417:56:27",   /* spread */
   "1994092518:21:20",   /* spread */
   "2001020818:46:13",   /* spread */
   "2008071919:11:06",   /* spread */
   "2015120219:35:59",   /* spread */
   "2022051320:00:52",   /* spread */
   "1933102420:25:45",   /* spread */
   "1940030720:50:38",   /* spread */
   "1947081821:15:31",   /* spread */
   "1954010121:40:24",   /* spread */
   "1961061222:05:17",   /* spread */
   "1968112322:30:10",   /* spread */
   "1975040622:55:03",   /* spread */
   "1982091723:19:56",   /* spread */
   "1989022823:44:49",   /* spread */
   "1996071100:09:42",   /* spread */
   "2003122200:34:35",   /* spread */
   "2010050500:59:28",   /* spread */
   "2017101601:24:21",   /* spread */
   "2024032701:49:14",   /* spread */
   "1935081002:14:07",   /* spread */
   "1942012102:39:00",   /* spread */
   "1949060403:03:53",   /* spread */
   "1956111503:28:46",   /* spread */
   "1963042603:53:39",   /* spread */
   "1970090904:18:32",   /* spread */
   "1977022004:43:25",   /* spread */
   "1984070305:08:18",   /* spread */
   "1991121405:33:11",   /* spread */
   "1998052505:58:04",   /* spread */
   "2005100806:22:57",   /* spread */
   "2012031906:47:50",   /* spread */
   "2019080207:12:43",   /* spread */
   "1930011307:37:36",   /* spread */
   "1937062408:02:29",   /* spread */
   "1944110708:27:22",   /* spread */
   "1951041808:52:15",   /* spread */
   "1958090109:17:08",   /* spread */
   "1965021209:42:01",   /* spread */
   "1972072310:06:54",   /* spread */
   "1979120610:31:47",   /* spread */
   "1986051710:56:40",   /* spread */
   "1993102811:21:33",   /* spread */
   "2000031111:46:26",   /* spread */
   "2007082212:11:19",   /* spread */
   "2014010512:36:12",   /* spread */
   "2021061613:01:05",   /* spread */
   "1932112713:25:58",   /* spread */
   "1939041013:50:51",   /* spread */
   "1946092114:15:44",   /* spread */
   "1953020414:40:37",   /* spread */
   "1960071515:05:30",   /* spread */
   "1967122615:30:23",   /* spread */
   "1974050915:55:16",   /* spread */
   "1981102016:20:09",   /* spread */
   "1988030316:45:02",   /* spread */
   "1995081417:09:55",   /* spread */
   "2002012517:34:48",   /* spread */
   "2009060817:59:41",   /* spread */
   "2016111918:24:34",   /* spread */
   "2023040218:49:27",   /* spread */
   "1934091319:14:20",   /* spread */
   "1941022419:39:13",   /* spread */
   "1948070720:04:06",   /* spread */
   "1955121820:28:59",   /* spread */
   "1962050120:53:52",   /* spread */
   "1969101221:18:45",   /* spread */
   "1976032321:43:38",   /* spread */
   "1983080622:08:31",   /* spread */
   "1990011722:33:24",   /* spread */
   "1997062822:58:17",   /* spread */
   "2004111123:23:10",   /* spread */
   "2011042223:48:03",   /* spread */
   "2018090500:12:56",   /* spread */
   "2025021600:37:49",   /* spread */
   "1936072701:02:42",   /* spread */
   "1943121001:27:35",   /* spread */
   "1950052101:52:28",   /* spread */
   "1957100402:17:21",   /* spread */
   "1964031502:42:14",   /* spread */
   "1971082603:07:07",   /* spread */
   "1978010903:32:00",   /* spread */
   "1985062003:56:53",   /* spread */
   "1992110304:21:46",   /* spread */
   "1999041404:46:39",   /* spread */
   "2006092505:11:32",   /* spread */
   "2013020805:36:25",   /* spread */
   "2020071906:01:18",   /* spread */
   "1931120206:26:11",   /* spread */
   "1938051306:51:04",   /* spread */
   "1945102407:15:57",   /* spread */
   "1952030707:40:50",   /* spread */
   "1959081808:05:43",   /* spread */
   "1966010108:30:36",   /* spread */
   "1973061208:55:29",   /* spread */
   "",                  /* blank datetime -- keys as day zero */
   "2026061512:00:00",  /* equal to the row above it in value, so the tag has a duplicate run */
};

/* ------------------------------------------------------------------- case */

int caseCdxTime( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name        type  len  dec  nulls */
      { (char *)"ID",   'I',   4,   0,   0 },
      { (char *)"TS",   'T',   8,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   /* name       expression  filter  unique  descending */
   static TAG4INFO tags[] =
   {
      { (char *)"T_TS",   (char *)"TS",  0,  0,  0 },
      { (char *)"T_TSD",  (char *)"TS",  0,  0,  r4descending },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   DATA4 *data;
   int i, rc;

   sprintf( path, "%s\\CDXTIME.DBF", outDir );
   printf( "CDXTIME.DBF (0x30 + CDX, datetime keys) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create CDXTIME.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)i );

      /* An empty string is left as the blank d4blank already wrote: eight zero
       * bytes, which t4dateTimeToFox reads as day zero. */
      if ( STAMPS[i][0] != '\0' )
         f4assignDateTime( d4fieldJ( data, 2 ), STAMPS[i] );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   if ( i4create( data, 0, tags ) == 0 )
   {
      d4close( data );
      return fail( cb, "i4create CDXTIME.CDX" );
   }
   if ( error4code( cb ) > 0 )
      error4set( cb, 0 );

   printf( "%d records, %d tags\n", ROWS, (int)( sizeof( tags ) / sizeof( tags[0] ) ) - 1 );

   rc = finish( cb, data, outDir, "CDXTIME.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "CDXTIME.DBF", "CDXTIME.cdx" );

   return rc;
}
