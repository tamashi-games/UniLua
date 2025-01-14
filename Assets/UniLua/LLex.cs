﻿
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

using NumberStyles = System.Globalization.NumberStyles;

namespace UniLua {
  public class LLex {
    public const char EOZ = Char.MaxValue;

    private LuaState Lua;
    private int Current;
    public int LineNumber;
    public int LastLine;
    private ILoadInfo LoadInfo;
    public string Source;

    public Token Token;
    private Token LookAhead;

    private StringBuilder _Saved;

    private StringBuilder Saved {
      get {
        if (_Saved == null) {
          _Saved = new StringBuilder();
        }

        return _Saved;
      }
    }

    private static Dictionary<string, TK> ReservedWordDict;

    static LLex() {
      ReservedWordDict = new Dictionary<string, TK>();
      ReservedWordDict.Add("and", TK.AND);
      ReservedWordDict.Add("break", TK.BREAK);
      ReservedWordDict.Add("do", TK.DO);
      ReservedWordDict.Add("else", TK.ELSE);
      ReservedWordDict.Add("elseif", TK.ELSEIF);
      ReservedWordDict.Add("end", TK.END);
      ReservedWordDict.Add("false", TK.FALSE);
      ReservedWordDict.Add("for", TK.FOR);
      ReservedWordDict.Add("function", TK.FUNCTION);
      ReservedWordDict.Add("goto", TK.GOTO);
      ReservedWordDict.Add("if", TK.IF);
      ReservedWordDict.Add("in", TK.IN);
      ReservedWordDict.Add("local", TK.LOCAL);
      ReservedWordDict.Add("nil", TK.NIL);
      ReservedWordDict.Add("not", TK.NOT);
      ReservedWordDict.Add("or", TK.OR);
      ReservedWordDict.Add("repeat", TK.REPEAT);
      ReservedWordDict.Add("return", TK.RETURN);
      ReservedWordDict.Add("then", TK.THEN);
      ReservedWordDict.Add("true", TK.TRUE);
      ReservedWordDict.Add("until", TK.UNTIL);
      ReservedWordDict.Add("while", TK.WHILE);
    }

    public LLex(ILuaState lua, ILoadInfo loadinfo, string name) {
      Lua = (LuaState) lua;
      LoadInfo = loadinfo;
      LineNumber = 1;
      LastLine = 1;
      Token = null;
      LookAhead = null;
      _Saved = null;
      Source = name;

      _Next();
    }

    public void Next() {
      LastLine = LineNumber;
      if (LookAhead != null) {
        Token = LookAhead;
        LookAhead = null;
      }
      else {
        Token = _Lex();
      }
    }

    public Token GetLookAhead() {
      Utl.Assert(LookAhead == null);
      LookAhead = _Lex();
      return LookAhead;
    }

    private void _Next() {
      var c = LoadInfo.ReadByte();
      Current = (c == -1) ? EOZ : c;
    }

    private void _SaveAndNext() {
      Saved.Append((char) Current);
      _Next();
    }

    private void _Save(char c) {
      Saved.Append(c);
    }

    private string _GetSavedString() {
      return Saved.ToString();
    }

    private void _ClearSaved() {
      _Saved = null;
    }

    private bool _CurrentIsNewLine() {
      return Current == '\n' || Current == '\r';
    }

    private bool _CurrentIsDigit() {
      return Char.IsDigit((char) Current);
    }

    private bool _CurrentIsXDigit() {
      return _CurrentIsDigit() ||
             ('A' <= Current && Current <= 'F') ||
             ('a' <= Current && Current <= 'f');
    }

    private bool _CurrentIsSpace() {
      return Char.IsWhiteSpace((char) Current);
    }

    private bool _CurrentIsAlpha() {
      return Char.IsLetter((char) Current);
    }

    private bool _IsReserved(string identifier, out TK type) {
      return ReservedWordDict.TryGetValue(identifier, out type);
    }

    public bool IsReservedWord(string name) {
      return ReservedWordDict.ContainsKey(name);
    }

    private void _IncLineNumber() {
      var old = Current;
      _Next();
      if (_CurrentIsNewLine() && Current != old)
        _Next();
      if (++LineNumber >= Int32.MaxValue)
        _Error("chunk has too many lines");
    }

    private string _ReadLongString(int sep) {
      _SaveAndNext();

      if (_CurrentIsNewLine())
        _IncLineNumber();

      while (true) {
        switch (Current) {
          case EOZ:
            _LexError(_GetSavedString(),
              "unfinished long string/comment",
              (int) TK.EOS);
            break;

          case '[': {
            if (_SkipSep() == sep) {
              _SaveAndNext();
              if (sep == 0) {
                _LexError(_GetSavedString(),
                  "nesting of [[...]] is deprecated",
                  (int) TK.EOS);
              }
            }

            break;
          }

          case ']': {
            if (_SkipSep() == sep) {
              _SaveAndNext();
              goto endloop;
            }

            break;
          }

          case '\n':
          case '\r': {
            _Save('\n');
            _IncLineNumber();
            break;
          }

          default: {
            _SaveAndNext();
            break;
          }
        }
      }

      endloop:
      var r = _GetSavedString();
      return r.Substring(2 + sep, r.Length - 2 * (2 + sep));
    }

    private void _EscapeError(string info, string msg) {
      _LexError("\\" + info, msg, (int) TK.STRING);
    }

    private byte _ReadHexEscape() {
      int r = 0;
      var c = new char[3] {'x', (char) 0, (char) 0};
      // read two hex digits
      for (int i = 1; i < 3; ++i) {
        _Next();
        c[i] = (char) Current;
        if (!_CurrentIsXDigit()) {
          _EscapeError(new String(c, 0, i + 1),
            "hexadecimal digit expected");
          // error
        }

        r = (r << 4) + Int32.Parse(Current.ToString(),
              NumberStyles.HexNumber);
      }

      return (byte) r;
    }

    private byte _ReadDecEscape() {
      int r = 0;
      var c = new char[3];
      // read up to 3 digits
      int i = 0;
      for (i = 0; i < 3 && _CurrentIsDigit(); ++i) {
        c[i] = (char) Current;
        r = r * 10 + Current - '0';
        _Next();
      }

      if (r > Byte.MaxValue)
        _EscapeError(new String(c, 0, i),
          "decimal escape too large");
      return (byte) r;
    }

    private string _ReadString() {
      var del = Current;
      _Next();
      while (Current != del) {
        switch (Current) {
          case EOZ:
            _Error("unfinished string");
            continue;

          case '\n':
          case '\r':
            _Error("unfinished string");
            continue;

          case '\\': {
            byte c;
            _Next();
            switch (Current) {
              case 'a':
                c = (byte) '\a';
                break;
              case 'b':
                c = (byte) '\b';
                break;
              case 'f':
                c = (byte) '\f';
                break;
              case 'n':
                c = (byte) '\n';
                break;
              case 'r':
                c = (byte) '\r';
                break;
              case 't':
                c = (byte) '\t';
                break;
              case 'v':
                c = (byte) '\v';
                break;
              case 'x':
                c = _ReadHexEscape();
                break;

              case '\n':
              case '\r':
                _Save('\n');
                _IncLineNumber();
                continue;

              case '\\':
              case '\"':
              case '\'':
                c = (byte) Current;
                break;

              case EOZ: continue;

              // zap following span of spaces
              case 'z': {
                _Next(); // skip `z'
                while (_CurrentIsSpace()) {
                  if (_CurrentIsNewLine())
                    _IncLineNumber();
                  else
                    _Next();
                }

                continue;
              }

              default: {
                if (!_CurrentIsDigit())
                  _EscapeError(Current.ToString(),
                    "invalid escape sequence");

                // digital escape \ddd
                c = _ReadDecEscape();
                _Save((char) c);
                continue;
                // {
                //     c = (char)0;
                //     for(int i=0; i<3 && _CurrentIsDigit(); ++i)
                //     {
                //         c = (char)(c*10 + Current - '0');
                //         _Next();
                //     }
                //     _Save( c );
                // }
                // continue;
              }
            }

            _Save((char) c);
            _Next();
            continue;
          }

          default:
            _SaveAndNext();
            continue;
        }
      }

      _Next();
      return _GetSavedString();
    }

    private double _ReadNumber() {
      var expo = new char[] {'E', 'e'};
      Utl.Assert(_CurrentIsDigit());
      var first = Current;
      _SaveAndNext();
      if (first == '0' && (Current == 'X' || Current == 'x')) {
        expo = new char[] {'P', 'p'};
        _SaveAndNext();
      }

      for (;;) {
        if (Current == expo[0] || Current == expo[1]) {
          _SaveAndNext();
          if (Current == '+' || Current == '-')
            _SaveAndNext();
        }

        if (_CurrentIsXDigit() || Current == '.')
          _SaveAndNext();
        else
          break;
      }

      double ret;
      var str = _GetSavedString();
      if (LuaState.O_Str2Decimal(str, out ret)) {
        return ret;
      }
      else {
        _Error("malformed number: " + str);
        return 0.0;
      }
    }

    // private float _ReadNumber()
    // {
    //     do
    //     {
    //         _SaveAndNext();
    //     } while( _CurrentIsDigit() || Current == '.' );
    //     if( Current == 'E' || Current == 'e' )
    //     {
    //         _SaveAndNext();
    //         if( Current == '+' || Current == '-' )
    //             _SaveAndNext();
    //     }
    //     while( _CurrentIsAlpha() || _CurrentIsDigit() || Current == '_' )
    //         _SaveAndNext();
    //     float ret;
    //     if( !Single.TryParse( _GetSavedString(), out ret ) )
    //         _Error( "malformed number" );
    //     return ret;
    // }

    private void _Error(string error) {
      Lua.O_PushString(string.Format(
        "{0}:{1}: {2}",
        Source, LineNumber, error));
      Lua.D_Throw(ThreadStatus.LUA_ERRSYNTAX);
    }

    private void _LexError(string info, string msg, int tokenType) {
      // TODO
      _Error(msg + ":" + info);
    }

    public void SyntaxError(string msg) {
      // TODO
      _Error(msg);
    }

    private int _SkipSep() {
      int count = 0;
      var boundary = Current;
      _SaveAndNext();
      while (Current == '=') {
        _SaveAndNext();
        count++;
      }

      return (Current == boundary ? count : (-count) - 1);
    }

    private Token _Lex() {
      _ClearSaved();
      while (true) {
        switch (Current) {
          case '\n':
          case '\r': {
            _IncLineNumber();
            continue;
          }

          case '-': {
            _Next();
            if (Current != '-') return new LiteralToken('-');

            // else is a long comment
            _Next();
            if (Current == '[') {
              int sep = _SkipSep();
              _ClearSaved();
              if (sep >= 0) {
                _ReadLongString(sep);
                _ClearSaved();
                continue;
              }
            }

            // else is a short comment
            while (!_CurrentIsNewLine() && Current != EOZ)
              _Next();
            continue;
          }

          case '[': {
            int sep = _SkipSep();
            if (sep >= 0) {
              string seminfo = _ReadLongString(sep);
              return new StringToken(seminfo);
            }
            else if (sep == -1) return new LiteralToken('[');
            else _Error("invalid long string delimiter");

            continue;
          }

          case '=': {
            _Next();
            if (Current != '=') return new LiteralToken('=');
            _Next();
            return new TypedToken(TK.EQ);
          }

          case '<': {
            _Next();
            if (Current != '=') return new LiteralToken('<');
            _Next();
            return new TypedToken(TK.LE);
          }

          case '>': {
            _Next();
            if (Current != '=') return new LiteralToken('>');
            _Next();
            return new TypedToken(TK.GE);
          }

          case '~': {
            _Next();
            if (Current != '=') return new LiteralToken('~');
            _Next();
            return new TypedToken(TK.NE);
          }

          case ':': {
            _Next();
            if (Current != ':') return new LiteralToken(':');
            _Next();
            return new TypedToken(TK.DBCOLON); // new in 5.2 ?
          }

          case '"':
          case '\'': {
            return new StringToken(_ReadString());
          }

          case '.': {
            _SaveAndNext();
            if (Current == '.') {
              _SaveAndNext();
              if (Current == '.') {
                _SaveAndNext();
                return new TypedToken(TK.DOTS);
              }
              else {
                return new TypedToken(TK.CONCAT);
              }
            }
            else if (!_CurrentIsDigit())
              return new LiteralToken('.');
            else
              return new NumberToken(_ReadNumber());
          }

          case EOZ: {
            return new TypedToken(TK.EOS);
          }

          default: {
            if (_CurrentIsSpace()) {
              _Next();
              continue;
            }
            else if (_CurrentIsDigit()) {
              return new NumberToken(_ReadNumber());
            }
            else if (_CurrentIsAlpha() || Current == '_') {
              do {
                _SaveAndNext();
              } while (_CurrentIsAlpha() ||
                       _CurrentIsDigit() ||
                       Current == '_');

              string identifier = _GetSavedString();
              TK type;
              if (_IsReserved(identifier, out type)) {
                return new TypedToken(type);
              }
              else {
                return new NameToken(identifier);
              }
            }
            else {
              var c = Current;
              _Next();
              return new LiteralToken(c);
            }
          }
        }
      }
    }

  }

}
