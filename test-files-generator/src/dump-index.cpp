/* ==========================================================================
 * dump-index.cpp — the expected-values dump written beside each generated
 * index file.
 *
 * EVERY VALUE HERE COMES FROM THE C LIBRARY'S OWN STRUCTURES, never from a
 * re-read of the file's bytes. That is the whole point (ADR-24). The DBF half
 * of the corpus dump reads its header raw, which is fair for a few shifts and
 * an or — but a CDX leaf is bit-packed, and it is the highest-risk decode in
 * the port. A generator that unpacked it here would only prove that our writer
 * and our reader misunderstand the format the same way.
 *
 * So: keys and record numbers come from tfile4top/tfile4dskip/tfile4key/
 * tfile4recNo; block structure comes from the live B4BLOCK at tfile4block();
 * and the per-entry duplicate/trail/record values come from the library's own
 * x4dupCnt/x4trailCnt/x4recNo macros (d4declar.h:1807-1854). d4check then
 * re-derives every key from the table's records and verifies key order, so the
 * reference implementation vouches for what is written here (i4check.c:127-323).
 *
 * Navigation order, not physical order. tfile4top on a descending tag lands at
 * the physical *bottom* (I4TAG.C:3285-3288) and tfile4dskip walks physically
 * backwards, so a descending tag dumps reversed — which is what "first to last"
 * means for that tag, and what the port has to reproduce.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "dump-index.h"

/* Blocks seen during a walk. Sized well above anything the corpus holds; the
 * writer fails loudly rather than silently dumping a partial tree. */
#define MAX4SEEN 8192

typedef struct
{
   unsigned long nodes[MAX4SEEN];
   int           count;
} SEEN4NODES;

/* 1 if the node had not been seen before, 0 if it had, -1 if the table is full. */
static int seenAdd( SEEN4NODES *seen, unsigned long node )
{
   int i;

   for ( i = 0; i < seen->count; i++ )
      if ( seen->nodes[i] == node )
         return 0;

   if ( seen->count >= MAX4SEEN )
      return -1;

   seen->nodes[ seen->count++ ] = node;
   return 1;
}

/* How many keys the tag holds, by walking it.
 *
 * NOT tfile4count: that function is **wrong for a descending tag**. It calls
 * tfile4top, which on a descending tag lands at the physical *bottom*
 * (I4TAG.C:3285-3288), and then skips forward with the physical tfile4skip
 * (I4TAG.C:1000-1019) — which from the bottom moves nowhere, so it returns 1
 * however many keys there are. tfile4dskip is the direction-aware skip
 * (I4TAG.C:65-89), and walking with it is the library's own navigation without
 * the miscount. */
static long countKeys( TAG4FILE *t4 )
{
   long count = 0;

   if ( tfile4top( t4 ) < 0 )
      return -1;

   if ( tfile4eof( t4 ) )
      return 0;

   for ( ;; )
   {
      count++;
      if ( count > 4000000L )        /* a runaway walk, not a corpus tag */
      {
         fprintf( stderr, "  ERROR: tag %s does not end\n", t4->alias );
         return -1;
      }
      if ( tfile4dskip( t4, 1L ) != 1L )
         return count;
   }
}

/* sortSeq is a char[8] holding a NUL-terminated name — "" for machine order,
 * "GENERAL", "CBnnnnn" (i4init.c:372-418). Dumped as the name, escaped. */
static void dumpSortSeq( FILE *out, const char *sortSeq )
{
   unsigned long len = 0;

   while ( len < 8 && sortSeq[len] != '\0' )
      len++;

   dumpEscapedBytes( out, sortSeq, len );
}

/* One block, from the library's parse of it. Node numbers are printed as
 * unsigned decimal with no interpretation: 0 and 4294967295 both occur as
 * "no neighbour" and which one a build path writes is a fact to gate, not a
 * fact to normalize away. */
static int dumpBlock( FILE *out, B4BLOCK *b4, int keyLen )
{
   int nKeys = b4numKeys( b4 );
   int isLeaf = ( b4->header.nodeAttribute & 0x02 ) != 0;   /* bit test, b4block.c:2003-2014 */
   int i;

   fprintf( out, "node=%lu attr=%d nKeys=%d left=%lu right=%lu",
            (unsigned long)b4node( b4->fileBlock ),
            (int)b4->header.nodeAttribute,
            nKeys,
            (unsigned long)b4node( b4->header.leftNode ),
            (unsigned long)b4node( b4->header.rightNode ) );

   if ( isLeaf )
   {
      unsigned long mask;

      memcpy( &mask, b4->nodeHdr.recNumMask, 4 );
      fprintf( out, " leaf=1 freeSpace=%d recNumLen=%u dupCntLen=%u trailCntLen=%u infoLen=%u"
                    " recNumMask=0x%08lX dupByteCnt=0x%02X trailByteCnt=0x%02X\n",
               (int)b4->nodeHdr.freeSpace,
               (unsigned)b4->nodeHdr.recNumLen,
               (unsigned)b4->nodeHdr.dupCntLen,
               (unsigned)b4->nodeHdr.trailCntLen,
               (unsigned)b4->nodeHdr.infoLen,
               mask,
               (unsigned)b4->nodeHdr.dupByteCnt,
               (unsigned)b4->nodeHdr.trailByteCnt );

      for ( i = 0; i < nKeys; i++ )
         fprintf( out, "  %d rec=%lu dup=%d trail=%d\n",
                  i,
                  (unsigned long)x4recNo( b4, i ),
                  (int)x4dupCnt( b4, i ),
                  (int)x4trailCnt( b4, i ) );
   }
   else
   {
      fprintf( out, " leaf=0\n" );

      /* Interior entries are keyLen+8 bytes packed from block offset 12, with
       * the record number and the child node both big-endian. b4key/b4recNo
       * are the library's own accessors for exactly that (b4block.c:1938-1946),
       * so the endianness is its opinion and not ours. */
      for ( i = 0; i < nKeys; i++ )
      {
         B4KEY_DATA *entry = b4key( b4, i );

         if ( entry == 0 )
         {
            fprintf( stderr, "  ERROR: b4key failed on branch entry %d\n", i );
            return 1;
         }

         fprintf( out, "  %d child=%lu rec=%lu key=",
                  i,
                  (unsigned long)entry->num,
                  (unsigned long)b4recNo( b4, i ) );
         dumpEscapedBytes( out, (const char *)entry->value, (unsigned long)keyLen );
         fprintf( out, "\n" );
      }
   }

   return 0;
}

/* Every block of the tag's tree, in the order a full walk reaches it.
 *
 * The root-to-leaf path lives in t4->blocks while the cursor is positioned
 * (tfile4up/tfile4down maintain it), so walking every key and dumping any path
 * block not yet dumped enumerates the whole tree — interior nodes included,
 * which is otherwise the hard part. */
static int dumpBlocks( FILE *out, TAG4FILE *t4, long count )
{
   SEEN4NODES seen;
   long i;

   seen.count = 0;

   fprintf( out, "[blocks]\n" );

   if ( tfile4top( t4 ) < 0 )
      return 1;

   for ( i = 0; i < count; i++ )
   {
      B4BLOCK *b4;

      for ( b4 = (B4BLOCK *)l4first( &t4->blocks ); b4 != 0;
            b4 = (B4BLOCK *)l4next( &t4->blocks, b4 ) )
      {
         int isNew = seenAdd( &seen, (unsigned long)b4node( b4->fileBlock ) );

         if ( isNew < 0 )
         {
            fprintf( stderr, "  ERROR: more than %d blocks in tag %s\n", MAX4SEEN, t4->alias );
            return 1;
         }
         if ( isNew && dumpBlock( out, b4, (int)t4->header.keyLen ) != 0 )
            return 1;
      }

      if ( i + 1 < count && tfile4dskip( t4, 1L ) != 1L )
      {
         fprintf( stderr, "  ERROR: tag %s ran out of keys after %ld of %ld\n",
                  t4->alias, i + 1, count );
         return 1;
      }
   }

   fprintf( out, "blocks %d\n", seen.count );
   return 0;
}

/* Every (key, record number) pair in navigation order. */
static int dumpKeys( FILE *out, TAG4FILE *t4, long count )
{
   int keyLen = (int)t4->header.keyLen;
   long i;

   fprintf( out, "[keys]\n" );

   if ( tfile4top( t4 ) < 0 )
      return 1;

   for ( i = 0; i < count; i++ )
   {
      char *key = tfile4key( t4 );

      if ( key == 0 )
      {
         fprintf( stderr, "  ERROR: tfile4key failed in tag %s at key %ld\n", t4->alias, i );
         return 1;
      }

      fprintf( out, "  " );
      dumpEscapedBytes( out, key, (unsigned long)keyLen );
      fprintf( out, " %lu\n", (unsigned long)tfile4recNo( t4 ) );

      if ( i + 1 < count && tfile4dskip( t4, 1L ) != 1L )
      {
         fprintf( stderr, "  ERROR: tag %s ran out of keys after %ld of %ld\n",
                  t4->alias, i + 1, count );
         return 1;
      }
   }

   if ( !tfile4eof( t4 ) && tfile4dskip( t4, 1L ) == 1L )
   {
      fprintf( stderr, "  ERROR: tag %s has more keys than the %ld it counted\n", t4->alias, count );
      return 1;
   }

   return 0;
}

/* The expression and filter text of a tag, as the library parsed them back.
 * Written on their own lines so a long expression cannot make the header line
 * unreadable. The tag directory has neither, and dumps both as empty. */
static void dumpTagText( FILE *out, TAG4FILE *t4 )
{
   const char *expr = ( t4->expr == 0 ) ? "" : expr4source( t4->expr );
   const char *filter = ( t4->filter == 0 ) ? "" : expr4source( t4->filter );

   if ( expr == 0 ) expr = "";
   if ( filter == 0 ) filter = "";

   fprintf( out, "expr         " );
   dumpEscapedBytes( out, expr, (unsigned long)strlen( expr ) );
   fprintf( out, "\nfilter       " );
   dumpEscapedBytes( out, filter, (unsigned long)strlen( filter ) );
   fprintf( out, "\n" );
}

/* One tag: its header, its blocks, its keys. `name` is the tag's alias, or
 * "*directory*" for the hidden tag-name B-tree, which is dumped by this same
 * function because it *is* a tag — keyLen 10, pad character ' ', and its
 * "record numbers" are the header node of each tag (CDX-FORMAT.md §2). */
static int dumpTag( FILE *out, TAG4FILE *t4, const char *name )
{
   T4HEADER *h = &t4->header;
   long count = countKeys( t4 );

   if ( count < 0 )
      return 1;

   fprintf( out, "\n[tag %s]\n", name );
   fprintf( out, "header       keyLen=%u typeCode=0x%02X signature=0x%02X descending=%u"
                 " pChar=0x%02X root=%lu freeList=%lu version=%lu headerNode=%lu\n",
            (unsigned)h->keyLen,
            (unsigned)h->typeCode,
            (unsigned)h->signature,
            (unsigned)h->descending,
            (unsigned)(unsigned char)t4->pChar,
            (unsigned long)b4node( h->root ),
            (unsigned long)b4node( h->freeList ),
            (unsigned long)h->version,
            (unsigned long)b4node( t4->headerOffset ) );
   fprintf( out, "text         exprPos=%u exprLen=%u filterPos=%u filterLen=%u sortSeq=",
            (unsigned)h->exprPos,
            (unsigned)h->exprLen,
            (unsigned)h->filterPos,
            (unsigned)h->filterLen );
   dumpSortSeq( out, h->sortSeq );
   fprintf( out, "\n" );
   dumpTagText( out, t4 );
   fprintf( out, "count        %ld\n", count );

   if ( count == 0 )
   {
      /* Nothing to walk, and nothing legitimate to say about blocks. */
      fprintf( out, "[blocks]\nblocks 0\n[keys]\n" );
      return 0;
   }

   if ( dumpBlocks( out, t4, count ) != 0 )
      return 1;

   return dumpKeys( out, t4, count );
}

int dumpIndex( CODE4 *cb, const char *outDir, const char *dbfName, const char *indexName )
{
   char dbfPath[520], indexPath[520], dumpPath[520], stem[280], ext[16];
   const char *dot;
   DATA4 *data;
   INDEX4 *index;
   INDEX4FILE *file;
   TAG4 *tag;
   FILE *out;
   int rc = 0;
   int savedAutoOpen;
   unsigned k;

   sprintf( dbfPath, "%s\\%s", outDir, dbfName );
   sprintf( indexPath, "%s\\%s", outDir, indexName );

   /* IDXONE.IDX -> "IDXONE" + "idx" -> IDXONE.idx.dump.txt */
   dot = strrchr( indexName, '.' );
   sprintf( stem, "%.*s", dot ? (int)( dot - indexName ) : (int)strlen( indexName ), indexName );
   sprintf( ext, "%s", dot ? dot + 1 : "idx" );
   for ( k = 0; ext[k] != '\0'; k++ )
      if ( ext[k] >= 'A' && ext[k] <= 'Z' )
         ext[k] = (char)( ext[k] - 'A' + 'a' );
   sprintf( dumpPath, "%s\\%s.%s.dump.txt", outDir, stem, ext );

   /* One dump concerns exactly one index file, so the production index is not
    * auto-opened and the wanted file is opened by name whether it is the
    * production one or not. d4check then has only this file to comment on,
    * which is what makes a failure attributable. */
   savedAutoOpen = cb->autoOpen;
   cb->autoOpen = 0;
   data = d4open( cb, dbfPath );
   cb->autoOpen = savedAutoOpen;

   if ( data == 0 )
      return fail( cb, "reopen for index dump" );

   index = i4open( data, indexPath );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4open for index dump" );
   }

   file = index->indexFile;

   out = fopen( dumpPath, "wb" );   /* "wb": LF endings on any host */
   if ( out == 0 )
   {
      fprintf( stderr, "  ERROR: cannot create %s\n", dumpPath );
      d4close( data );
      return 1;
   }

   fprintf( out, "# CodeBase.NET corpus index dump\n" );
   fprintf( out, "# Generated by test-files-generator from the original CodeBase C library.\n" );
   fprintf( out, "# Do not edit by hand; regenerate instead.\n" );
   fprintf( out, "file         %s\n", indexName );
   fprintf( out, "table        %s\n", dbfName );
   fprintf( out, "shape        %s\n",
            file->tagIndex->header.typeCode >= 64 ? "compound" : "single-tag" );
   fprintf( out, "blockSize    %lu\n", (unsigned long)file->blockSize );
   fprintf( out, "multiplier   %lu\n", (unsigned long)file->multiplier );
   fprintf( out, "codeBaseNote 0x%08lX\n", (unsigned long)file->tagIndex->header.codeBaseNote );

   /* d4check walks every tag of every open index in key order, re-evaluating
    * each key expression per record and comparing it with the stored key
    * (i4check.c:127-323). It is the reference implementation certifying the
    * file, so a failure here fails the case.
    *
    * Except on a single-tag file, which **d4check cannot check at all**.
    * i4checkBlocks flags the tag-directory header's two blocks first
    * (flagNo = headerOffset, i4check.c:889-894) and then flags each tag's header
    * in turn (i4check.c:905-914) — but in a single-tag file the tag list holds
    * the tagIndex itself (i4index.c:1824), whose headerOffset is 0, so the flag
    * is already set and it returns e4index. Every .IDX fails by construction,
    * whoever wrote it. Such a case is witnessed differently: the same tree read
    * through both file shapes must yield the same keys (case-idxone.cpp). */
   if ( file->tagIndex->header.typeCode < 64 )
   {
      fprintf( out, "check        skipped-single-tag\n" );
   }
   else if ( d4check( data ) < 0 )
   {
      fprintf( out, "check        FAILED\n" );
      fprintf( stderr, "  ERROR: d4check failed on %s (errorCode=%d, errorCode2=%ld)\n",
               indexName, (int)error4code( cb ), (long)cb->errorCode2 );
      rc = 1;
   }
   else
   {
      fprintf( out, "check        ok\n" );
   }

   if ( rc == 0 && file->tagIndex->header.typeCode >= 64 )
      rc = dumpTag( out, file->tagIndex, "*directory*" );

   for ( tag = 0; rc == 0; )
   {
      tag = d4tagNext( data, tag );
      if ( tag == 0 )
         break;

      /* d4tagNext walks every tag of every open index; keep this file's. */
      if ( tag->index != index )
         continue;

      rc = dumpTag( out, tag->tagFile, tag->tagFile->alias );
   }

   fclose( out );

   if ( d4close( data ) < 0 )
      return fail( cb, "d4close after index dump" );

   if ( rc == 0 )
      printf( "  dump -> %s.%s.dump.txt\n", stem, ext );

   return rc;
}
