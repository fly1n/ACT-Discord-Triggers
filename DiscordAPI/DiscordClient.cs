using Discord;
using Discord.Audio;
using Discord.WebSocket;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading.Tasks;

namespace DiscordAPI
{
  public class DiscordClient
  {
    private DiscordSocketClient bot;
    private IAudioClient audioClient;
    private AudioOutStream voiceStream;

    private string tts_voice;
    private int tts_vol;
    private int tts_speed;

    public delegate void BotLoaded();
    public BotLoaded BotReady;

    public delegate void BotMessage(string message);
    public BotMessage Log;

    public delegate void GetTTSSetting(out string voice, out int vol, out int speed);
    public GetTTSSetting FetchSettingFunc;

    public async void InIt(string bot_token)
    {
      try
      {
        bot = new DiscordSocketClient();
      }
      catch (NotSupportedException ex)
      {
        Log?.Invoke("Unsupported Operating System.");
        Log?.Invoke(ex.Message);
      }

      try
      {
        bot.Log += Bot_Log;
        bot.Ready += Bot_Ready;
        bot.MessageReceived += Bot_MessageReceived;
        await bot.LoginAsync(TokenType.Bot, bot_token);
        await bot.StartAsync();
      }
      catch (Exception ex)
      {
        Log?.Invoke(ex.Message);
        Log?.Invoke("Error connecting to Discord.");
      }
    }

    public async Task deInIt()
    {
      bot.Ready -= Bot_Ready;
      await bot.StopAsync();
      await bot.LogoutAsync();
      if (audioClient?.ConnectionState == ConnectionState.Connected)
      {
        voiceStream?.Close();
        await audioClient.StopAsync();
      }
    }

    private Task Bot_Log(LogMessage arg)
    {
      if (arg.Message.Equals("Unknown OpCode (Hello)"))
        return Task.CompletedTask;
      Log?.Invoke($"[{arg.Source}] {arg.Message}");
      return Task.CompletedTask;
    }

    private async Task Bot_Ready()
    {
      await bot.SetGameAsync("Mashiro");
      Log?.Invoke("TTS Bot in ready state. Populating servers...");
      BotReady?.Invoke();
    }

    private Task Bot_MessageReceived(SocketMessage message)
    {
      // check if the message is a user message as opposed to a system message (e.g. Clyde, pins, etc.)
      if (!(message is SocketUserMessage userMessage)) return Task.CompletedTask;
      // check if the message origin is a guild message channel
      if (!(userMessage.Channel is SocketTextChannel textChannel)) return Task.CompletedTask;
      // trigger TTS
      if (message.Content.StartsWith("!tts "))
      {
        FetchSettingFunc?.Invoke(out tts_voice, out tts_vol, out tts_speed);
        Speak(message.Content.Substring(5), tts_voice, tts_vol, tts_speed);
      }
      return Task.CompletedTask;
    }

    public bool IsConnected()
    {
      return bot?.ConnectionState == ConnectionState.Connected;
    }

    #region tts bot related
    public string[] getServers()
    {
      List<string> servers = new List<string>();

      try
      {
        foreach (SocketGuild g in bot.Guilds)
          servers.Add(g.Name);
      }
      catch (Exception ex)
      {
        Log?.Invoke("Error loading servers in DiscordAPI#getServers().");
        Log?.Invoke(ex.Message);
      }

      return servers.ToArray();
    }

    public string[] getChannels(string server)
    {
      List<string> discordchannels = new List<string>();

      foreach (SocketGuild g in bot.Guilds)
      {
        if (g.Name == server)
        {
          var channels = new List<SocketVoiceChannel>(g.VoiceChannels);
          channels.Sort((x, y) => x.Position.CompareTo(y.Position));
          foreach (SocketVoiceChannel channel in channels)
            discordchannels.Add(channel.Name);
          break;
        }
      }

      return discordchannels.ToArray();
    }

    private SocketVoiceChannel[] getSocketChannels(string server)
    {
      List<SocketVoiceChannel> discordchannels = new List<SocketVoiceChannel>();

      foreach (SocketGuild g in bot.Guilds)
      {
        if (g.Name == server)
        {
          var channels = new List<SocketVoiceChannel>(g.VoiceChannels);
          channels.Sort((x, y) => x.Position.CompareTo(y.Position));
          foreach (SocketVoiceChannel channel in channels)
            discordchannels.Add(channel);
          break;
        }
      }

      return discordchannels.ToArray();
    }

    public async Task<bool> JoinChannel(string server, string channel)
    {
      SocketVoiceChannel chan = null;

      foreach (SocketVoiceChannel vchannel in getSocketChannels(server))
        if (vchannel.Name == channel)
          chan = vchannel;

      if (chan != null)
      {
        try
        {
          audioClient = await chan.ConnectAsync();
          Log?.Invoke("Joined channel: " + chan.Name);
        }
        catch (Exception ex)
        {
          Log?.Invoke("Error joining channel.");
          Log?.Invoke(ex.Message);
          return false;
        }
      }
      return true;
    }

    public async void LeaveChannel()
    {
      voiceStream?.Close();
      voiceStream = null;
      await audioClient.StopAsync();
    }

    private object speaklock = new object();
    private SpeechAudioFormatInfo formatInfo = new SpeechAudioFormatInfo(48000, AudioBitsPerSample.Sixteen, AudioChannel.Stereo);

    public void Speak(string text, string voice, int vol, int speed)
    {
      lock (speaklock)
      {
        if (voiceStream == null)
          voiceStream = audioClient.CreatePCMStream(AudioApplication.Voice, 128 * 1024);
        SpeechSynthesizer tts = new SpeechSynthesizer();
        tts.SelectVoice(voice);
        tts.Volume = vol * 5;
        tts.Rate = speed - 10;
        MemoryStream ms = new MemoryStream();
        tts.SetOutputToAudioStream(ms, formatInfo);

        tts.Speak(text);
        ms.Seek(0, SeekOrigin.Begin);
        ms.CopyTo(voiceStream);
        voiceStream.Flush();
      }
    }

    public void SpeakFile(string path)
    {
      lock (speaklock)
      {
        if (voiceStream == null)
          voiceStream = audioClient.CreatePCMStream(AudioApplication.Voice, 128 * 1024);
        try
        {
          WaveFileReader wav = new WaveFileReader(path);
          WaveFormat waveFormat = new WaveFormat(48000, 16, 2);
          WaveStream pcm = WaveFormatConversionStream.CreatePcmStream(wav);
          WaveFormatConversionStream output = new WaveFormatConversionStream(waveFormat, pcm);
          output.CopyTo(voiceStream);
          voiceStream.Flush();
        }
        catch (Exception ex)
        {
          Log?.Invoke("Unable to read file: " + ex.Message);
        }
      }
    }
    #endregion
  }
}
