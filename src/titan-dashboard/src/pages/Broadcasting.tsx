import { useState, useCallback, useEffect } from 'react';
import { useMutation } from '@tanstack/react-query';
import { broadcastApi } from '../api/client';
import { Radio, Send, AlertCircle, CheckCircle, Clock, MessageSquare } from 'lucide-react';
import type { ServerMessage, SendBroadcastRequest, ServerMessageType } from '../types';
import { ServerMessageType as MessageType } from '../types';
import './DataPage.css';
import './Broadcasting.css';

const MESSAGE_TYPE_OPTIONS = [
  { value: MessageType.Info, label: 'Info', description: 'General informational message' },
  { value: MessageType.Warning, label: 'Warning', description: 'Warning that requires attention' },
  { value: MessageType.Error, label: 'Error', description: 'Error or critical alert' },
  { value: MessageType.Achievement, label: 'Achievement', description: 'Player achievement announcement' },
  { value: MessageType.Maintenance, label: 'Maintenance', description: 'Server maintenance notification' },
  { value: MessageType.Custom, label: 'Custom', description: 'Custom message with client-defined styling' },
];

export function BroadcastingPage() {
  const [content, setContent] = useState('');
  const [title, setTitle] = useState('');
  const [messageType, setMessageType] = useState<ServerMessageType>(MessageType.Info);
  const [iconId, setIconId] = useState('');
  const [durationSeconds, setDurationSeconds] = useState<string>('');
  const [recentMessages, setRecentMessages] = useState<ServerMessage[]>([]);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const sendMutation = useMutation({
    mutationFn: (request: SendBroadcastRequest) => broadcastApi.send(request),
    onSuccess: (message) => {
      setRecentMessages(prev => [message, ...prev].slice(0, 10));
      setContent('');
      setTitle('');
      setMessageType(MessageType.Info);
      setIconId('');
      setDurationSeconds('');
      setSuccessMessage('Broadcast sent successfully!');
    },
  });

  // Clear success message after 3 seconds
  useEffect(() => {
    if (successMessage) {
      const timeout = setTimeout(() => setSuccessMessage(null), 3000);
      return () => clearTimeout(timeout);
    }
  }, [successMessage]);

  const handleSubmit = useCallback((e: React.FormEvent) => {
    e.preventDefault();
    
    const request: SendBroadcastRequest = {
      content: content.trim(),
      type: messageType,
    };

    if (title.trim()) request.title = title.trim();
    if (iconId.trim()) request.iconId = iconId.trim();
    if (durationSeconds) {
      const duration = parseInt(durationSeconds, 10);
      if (!isNaN(duration) && duration > 0) {
        request.durationSeconds = duration;
      }
    }

    sendMutation.mutate(request);
  }, [content, title, messageType, iconId, durationSeconds, sendMutation.mutate]);

  const formatDateTime = (dateString: string) => {
    return new Date(dateString).toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  };

  const getTypeBadgeClass = (type: ServerMessageType) => {
    switch (type) {
      case MessageType.Warning: return 'badge-warning';
      case MessageType.Error: return 'badge-error';
      case MessageType.Achievement: return 'badge-success';
      case MessageType.Maintenance: return 'badge-warning';
      default: return 'badge-primary';
    }
  };

  const getTypeName = (type: ServerMessageType) => {
    return MESSAGE_TYPE_OPTIONS.find(opt => opt.value === type)?.label ?? 'Unknown';
  };

  const isFormValid = content.trim().length > 0 && content.length <= 2000;

  return (
    <div className="data-page">
      <div className="page-header">
        <div>
          <h1><Radio size={24} className="page-icon" /> Broadcasting</h1>
          <p className="subtitle">Send server-wide messages to all connected players</p>
        </div>
      </div>

      <div className="broadcast-layout">
        {/* Compose Form */}
        <div className="card broadcast-form-card">
          <div className="card-header">
            <h2 className="section-title">
              <MessageSquare size={18} />
              Compose Message
            </h2>
          </div>
          <div className="card-body">
            <form onSubmit={handleSubmit} className="broadcast-form">
              <div className="form-group">
                <label htmlFor="content" className="form-label">
                  Message Content <span className="required">*</span>
                </label>
                <textarea
                  id="content"
                  className="input broadcast-textarea"
                  value={content}
                  onChange={(e) => setContent(e.target.value)}
                  placeholder="Enter your broadcast message..."
                  maxLength={2000}
                  rows={4}
                  required
                />
                <div className="char-count">
                  <span className={content.length > 1900 ? 'char-warning' : ''}>
                    {content.length} / 2000
                  </span>
                </div>
              </div>

              <div className="broadcast-form-row">
                <div className="form-group">
                  <label htmlFor="title" className="form-label">Title (optional)</label>
                  <input
                    id="title"
                    type="text"
                    className="input"
                    value={title}
                    onChange={(e) => setTitle(e.target.value)}
                    placeholder="Message title..."
                    maxLength={100}
                  />
                </div>

                <div className="form-group">
                  <label htmlFor="messageType" className="form-label">Message Type</label>
                  <select
                    id="messageType"
                    className="input"
                    value={messageType}
                    onChange={(e) => setMessageType(Number(e.target.value) as ServerMessageType)}
                  >
                    {MESSAGE_TYPE_OPTIONS.map(opt => (
                      <option key={opt.value} value={opt.value}>
                        {opt.label}
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="broadcast-form-row">
                <div className="form-group">
                  <label htmlFor="iconId" className="form-label">Icon ID (optional)</label>
                  <input
                    id="iconId"
                    type="text"
                    className="input"
                    value={iconId}
                    onChange={(e) => setIconId(e.target.value)}
                    placeholder="e.g., trophy, alert..."
                    maxLength={100}
                  />
                </div>

                <div className="form-group">
                  <label htmlFor="duration" className="form-label">Duration (seconds)</label>
                  <input
                    id="duration"
                    type="number"
                    className="input"
                    value={durationSeconds}
                    onChange={(e) => setDurationSeconds(e.target.value)}
                    placeholder="Auto-dismiss after..."
                    min={1}
                    max={3600}
                  />
                </div>
              </div>

              {sendMutation.isError && (
                <div className="alert alert-danger" role="alert">
                  <AlertCircle size={16} />
                  <span>{sendMutation.error?.message || 'Failed to send broadcast'}</span>
                </div>
              )}

              {/* Success message */}
              {successMessage && (
                <div className="alert alert-success" role="status">
                  <CheckCircle size={16} />
                  <span>{successMessage}</span>
                </div>
              )}

              <button
                type="submit"
                className="btn btn-primary btn-lg broadcast-submit"
                disabled={!isFormValid || sendMutation.isPending}
              >
                {sendMutation.isPending ? (
                  <>
                    <span className="spinner" />
                    Sending...
                  </>
                ) : (
                  <>
                    <Send size={18} />
                    Send Broadcast
                  </>
                )}
              </button>
            </form>
          </div>
        </div>

        {/* Recent Messages */}
        <div className="card recent-messages-card">
          <div className="card-header">
            <h2 className="section-title">
              <Clock size={18} />
              Recent Broadcasts
            </h2>
          </div>
          <div className="card-body">
            {recentMessages.length === 0 ? (
              <div className="empty-state">
                <p>No broadcasts sent this session.</p>
              </div>
            ) : (
              <div className="recent-messages-list">
                {recentMessages.map((msg) => (
                  <div key={msg.messageId} className="recent-message">
                    <div className="message-header">
                      <span className={`badge ${getTypeBadgeClass(msg.type)}`}>
                        {getTypeName(msg.type)}
                      </span>
                      <span className="message-time">{formatDateTime(msg.timestamp)}</span>
                    </div>
                    {msg.title && <div className="message-title">{msg.title}</div>}
                    <div className="message-content">{msg.content}</div>
                    {msg.durationSeconds && (
                      <div className="message-meta">
                        Duration: {msg.durationSeconds}s
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
