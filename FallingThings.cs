//------------------------------------------------------------------------------
// <copyright file="FallingThings.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

// This module contains code to do display falling shapes, and do
// hit testing against a set of segments provided by the Kinect NUI, and
// have shapes react accordingly.

using System.Linq;
using System.Windows.Media.Imaging;

namespace TriviaGame
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Media;
	using System.Windows.Shapes;
	using Utils;

	// FallingThings is the main class to draw and maintain positions of falling shapes.  It also does hit testing
	// and appropriate bouncing.
	public class FallingThings
	{

		public FallingThings(int maxThings, double framerate, int intraFrames)
		{
			this.maxThings = maxThings;
			this.intraFrames = intraFrames;
			targetFrameRate = framerate * intraFrames;
			SetGravity(gravityFactor);
			sceneRect.X = sceneRect.Y = 0;
			sceneRect.Width = sceneRect.Height = 100;
			shapeSize = sceneRect.Height * baseShapeSize / 1000.0;
			expandingRate = Math.Exp(Math.Log(6.0) / (targetFrameRate * DissolveTime));
			randomYPosition = new Random();
			trivia = new Trivia();
			questions = trivia.Questions.ToList();
			game = new Game { GamDateTime = DateTime.Now };
			difficultyOptions = trivia.Answers.Where(diff => diff.AnsQueID == 3).ToList();

		}

		#region Members

		public List<Answer> difficultyOptions = new List<Answer>();
		private const double BaseGravity = 0.017;
		private const double BaseAirFriction = 0.994;
		//private List<DropItem> dropItems = new List<DropItem>();
		private List<Answer> dropItems = new List<Answer>();
		private readonly List<Thing> things = new List<Thing>();
		private List<Question> questions = new List<Question>();
		public List<Guess> guesses = new List<Guess>();
		private readonly Random rnd = new Random();
		private int maxThings;
		private readonly int intraFrames = 1;
		private readonly Dictionary<int, int> scores = new Dictionary<int, int>();
		private const double DissolveTime = 0.4;
		private Rect sceneRect;
		private double targetFrameRate = 60;
		private double dropRate = 1.0;
		private double shapeSize = 20.0;
		private double baseShapeSize = 20;
		private GameMode gameMode = GameMode.Off;
		private double gravity = BaseGravity;
		private double gravityFactor = 1.0;
		private double airFriction = BaseAirFriction;
		private int frameCount;
		private bool doRandomColors = false;
		private double expandingRate = 1.0;
		private Color baseColor = Color.FromRgb(0, 0, 0);
		private DropType polyTypes = DropType.All;
		private DateTime gameStartTime;
		public List<DropType> alltypes;
		private Question currentQuestion;
		private int currentQuestionID;
		private Random randomYPosition;
		private Trivia trivia;
		private Game game;
		public bool ready = false;

		public enum ThingState
		{
			Falling = 0,
			Bouncing = 1,
			Dissolving = 2,
			Remove = 3
		}

		public enum Difficulty
		{
			Easy = 1,
			Harder = 2
		}

		#endregion

		#region Gameplay Setup

		public void SetFramerate(double actualFramerate)
		{
			targetFrameRate = actualFramerate * intraFrames;
			expandingRate = Math.Exp(Math.Log(6.0) / (targetFrameRate * DissolveTime));
			if (gravityFactor != 0)
			{
				SetGravity(gravityFactor);
			}
		}

		public void SetBoundaries(Rect r)
		{
			sceneRect = r;
			shapeSize = r.Height * baseShapeSize / 1000.0;
		}

		public void SetGameDifficulty(int difficulty)
		{
			game.GamDifficulty = difficulty;

			//if we have both game difficulty and player name set, insert game object into database and set ready = true
			ready = true;
		}

		public void SetDropRate(double f)
		{
			dropRate = f;
		}

		public void SetSize(double f)
		{
			baseShapeSize = f;
			shapeSize = sceneRect.Height * baseShapeSize / 1000.0;
		}

		public void SetShapesColor(Color color, bool doRandom)
		{
			doRandomColors = doRandom;
			baseColor = color;
		}

		public void SetGameMode(GameMode mode)
		{
			gameMode = mode;
			gameStartTime = DateTime.Now;
			scores.Clear();
		}

		public void SetGravity(double f)
		{
			gravityFactor = f;
			gravity = f * BaseGravity / targetFrameRate / Math.Sqrt(targetFrameRate) / Math.Sqrt(intraFrames);
			airFriction = f == 0 ? 0.997 : Math.Exp(Math.Log(1.0 - ((1.0 - BaseAirFriction) / f)) / intraFrames);

			if (f == 0)
			{
				// Stop all movement as well!
				for (int i = 0; i < things.Count; i++)
				{
					Thing thing = things[i];
					thing.XVelocity = thing.YVelocity = 0;
					things[i] = thing;
				}
			}
		}

		public void SetPolies(DropType polies)
		{
			polyTypes = polies;
		}

		#endregion

		#region Gameplay functions

		public void Reset()
		{
			for (int i = 0; i < things.Count; i++)
			{
				Thing thing = things[i];
				if ((thing.State == ThingState.Bouncing) || (thing.State == ThingState.Falling))
				{
					thing.State = ThingState.Dissolving;
					thing.Dissolve = 0;
					things[i] = thing;
				}
			}

			gameStartTime = DateTime.Now;
			scores.Clear();
		}

		public void AskNewQuestion()
		{
			things.Clear();
			//Grab random question from questions
			//Set question to inactive

			if (!ready)
			{
				//Ask player for game difficulty
				currentQuestion = questions.Where(que => que.QueID == 3).First();
				questions.Remove(currentQuestion);
				dropItems = trivia.Answers.Where(ans => ans.AnsQueID == currentQuestion.QueID).ToList();
				maxThings = dropItems.Count();
			}
			else
			{
				currentQuestion = questions[randomYPosition.Next(0, questions.Count - 1)];
				questions.Remove(currentQuestion);
				dropItems = trivia.Answers.Where(ans => ans.AnsQueID == currentQuestion.QueID).ToList();
				maxThings = dropItems.Count();
			}

		}

		public HitType LookForHits(Dictionary<Bone, BoneData> segments, int playerId)
		{
			DateTime cur = DateTime.Now;
			HitType allHits = HitType.None;

			// Zero out score if necessary
			if (!scores.ContainsKey(playerId))
			{
				scores.Add(playerId, 0);
			}

			foreach (var pair in segments)
			{
				for (int i = 0; i < things.Count; i++)
				{
					HitType hit = HitType.None;
					Thing thing = things[i];
					switch (thing.State)
					{
						case ThingState.Bouncing:
						case ThingState.Falling:
							{
								var hitCenter = new Point(0, 0);
								double lineHitLocation = 0;
								Segment seg = pair.Value.GetEstimatedSegment(cur);
								if (thing.Hit(seg, ref hitCenter, ref lineHitLocation))
								{
									if (seg.IsCircle())
									{
										if (thing.thingType == ThingType.Difficulty)
										{
											SetGameDifficulty(Convert.ToInt32(difficultyOptions[thing.dropItemIndex].AnsText));
											dropItems.Clear();
											things.Clear();
										}
										else
										{
											Guess guess = new Guess { GueAnsID = dropItems[thing.dropItemIndex].AnsID };
											if (Convert.ToBoolean(thing.correctAnswer))
											{
												hit |= HitType.Correct;
												AddToScore(thing.TouchedBy, 5, thing.Center);
												//if there are no more correct answers, get rid of all balloons.
												dropItems[thing.dropItemIndex].AnsInactive = true;
												bool moreCorrectAnswers = false;

												foreach (Answer answer in dropItems.Where(answer => Convert.ToBoolean(answer.AnsCorrect) && Convert.ToBoolean(answer.AnsInactive == false)))
												{
													moreCorrectAnswers = true;
												}

												if (!moreCorrectAnswers)
												{
													foreach (Answer answer in dropItems)
													{
														answer.AnsInactive = true;
													}
												}
											}
											else
											{
												hit |= HitType.Incorrect;
											}
											//dropItems[thing.dropItemIndex].AnsInactive = true;
											thing.State = ThingState.Remove;
											things[i] = thing;
										}
									}
								}
							}

							break;
					}

					allHits |= hit;
				}
			}

			return allHits;
		}

		public void AdvanceFrame()
		{
			// Move all things by one step, accounting for gravity
			for (int thingIndex = 0; thingIndex < things.Count; thingIndex++)
			{
				Thing thing = things[thingIndex];
				thing.Center.Offset(thing.XVelocity, thing.YVelocity);
				thing.YVelocity += gravity * sceneRect.Height;
				thing.YVelocity *= airFriction;
				thing.XVelocity *= airFriction;
				thing.Theta += thing.SpinRate;

				// bounce off walls
				if ((thing.Center.X - thing.Size < 0) || (thing.Center.X + thing.Size > sceneRect.Width))
				{
					thing.XVelocity = -thing.XVelocity;
					thing.Center.X += thing.XVelocity;
				}

				// Then get rid of one if any that fall off the bottom
				if ((thing.Center.Y - thing.Size > sceneRect.Bottom) || (dropItems[thing.dropItemIndex].AnsInactive))
				{
					thing.State = ThingState.Remove;

				}


				// Get rid of after dissolving.
				if (thing.State == ThingState.Dissolving)
				{
					thing.Dissolve += 1 / (targetFrameRate * DissolveTime);
					thing.Size *= expandingRate;
					if (thing.Dissolve >= 1.0)
					{
						//xxx this should remove any answers that fall off the screen. We don't want them reappearing.
						dropItems[thing.dropItemIndex].AnsInactive = true;
						thing.State = ThingState.Remove;
					}
				}

				things[thingIndex] = thing;
			}

			// Then remove any that should go away now
			for (int i = 0; i < things.Count; i++)
			{
				Thing thing = things[i];
				if (thing.State == ThingState.Remove)
				{
					things.Remove(thing);
					i--;
				}
			}

			bool condition1 = things.Count < maxThings;
			double nextDouble = rnd.NextDouble();
			double answerDouble = dropRate/targetFrameRate;
			bool condition2 = nextDouble < dropRate / targetFrameRate;
			bool condition3 = polyTypes != DropType.None;

			int dropItemsCount = dropItems.Where(drp => drp.AnsInactive == false).Count();


			// Create any new things to drop based on dropRate
			if ((things.Count < maxThings) && (rnd.NextDouble() < dropRate / targetFrameRate) && (polyTypes != DropType.None))
			{
				//if they're all inactive, ask a new question
				if (dropItems.Where(ans => ans.AnsInactive).Count() == dropItems.Count())
				{
					AskNewQuestion();
				}

				foreach (Answer answer in dropItems)
				{
					if (!Convert.ToBoolean(answer.AnsInactive))
					{
						ThingType ttype = answer.AnsQueID == 3 ? ThingType.Difficulty : ThingType.Answer;
						DropNewThing(answer, shapeSize, Color.FromRgb(255, 255, 255), ttype);
					}
				}
			}
		}

		public void DrawFrame(UIElementCollection children)
		{
			frameCount++;

			// Draw all shapes in the scene
			for (int i = 0; i < things.Count; i++)
			{
				Thing thing = things[i];
				if (thing.Brush == null)
				{
					thing.Brush = new SolidColorBrush(thing.Color);
					double factor = 0.4 + (((double)thing.Color.R + thing.Color.G + thing.Color.B) / 1600);
					thing.Brush2 =
						new SolidColorBrush(
							Color.FromRgb(
								(byte)(255 - ((255 - thing.Color.R) * factor)),
								(byte)(255 - ((255 - thing.Color.G) * factor)),
								(byte)(255 - ((255 - thing.Color.B) * factor))));
					thing.BrushPulse = new SolidColorBrush(Color.FromRgb(255, 255, 255));
				}

				if (thing.State == ThingState.Bouncing)
				{
					// Pulsate edges
					double alpha = Math.Cos((0.15 * (thing.FlashCount++) * thing.Hotness) * 0.5) + 0.5;

					children.Add(
						MakeSimpleShape(
							1,
							thing.Size,
							thing.Theta,
							thing.Center,
							thing.Brush,
							thing.BrushPulse,
							thing.Size * 0.1,
							alpha,
							dropItems[thing.dropItemIndex].AnsImagePath));
					things[i] = thing;
				}
				else
				{
					if (thing.State == ThingState.Dissolving)
					{
						thing.Brush.Opacity = 1.0 - (thing.Dissolve * thing.Dissolve);
					}

					children.Add(
						MakeSimpleShape(
							1,
							thing.Size,
							thing.Theta,
							thing.Center,
							thing.Brush,
							(thing.State == ThingState.Dissolving) ? null : thing.Brush2,
							1,
							1,
							dropItems[thing.dropItemIndex].AnsImagePath));
				}
			}

			// Show scores
			//if (this.scores.Count != 0)
			if (1 == 1)
			{
				int i = 0;
				foreach (var score in scores)
				{
					Label label = MakeSimpleLabel(
						score.Value.ToString(CultureInfo.InvariantCulture),
						new Rect(
							(0.02 + (i * 0.6)) * sceneRect.Width,
							0.01 * sceneRect.Height,
							0.4 * sceneRect.Width,
							0.3 * sceneRect.Height),
							new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)));
					label.FontSize = Math.Max(1, Math.Min(sceneRect.Width / 12, sceneRect.Height / 12));
					label.FontFamily = new FontFamily("Century Regular");
					children.Add(label);
					i++;
				}
			}



			// Show game timer
			if (gameMode != GameMode.Off || 1 == 1)
			{
				TimeSpan span = DateTime.Now.Subtract(gameStartTime);
				string text = span.Minutes.ToString(CultureInfo.InvariantCulture) + ":" + span.Seconds.ToString("00");

				Label timeText = MakeSimpleLabel(
					text,
					new Rect(
						0.1 * sceneRect.Width, 0.25 * sceneRect.Height, 0.89 * sceneRect.Width, 0.72 * sceneRect.Height),
					new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)));
				timeText.FontSize = Math.Max(1, sceneRect.Height / 16);
				timeText.HorizontalContentAlignment = HorizontalAlignment.Right;
				timeText.VerticalContentAlignment = VerticalAlignment.Bottom;
				children.Add(timeText);
			}



			//Question label
			Label testText = MakeSimpleLabel(
					currentQuestion.QueQuestion + "?",
					new Rect(
						0,    //x position
						0,  //y position
						sceneRect.Width,   //width
						75), //height
					new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)));
			testText.FontSize = Math.Max(1, sceneRect.Height / 20);
			testText.HorizontalContentAlignment = HorizontalAlignment.Center;
			testText.VerticalContentAlignment = VerticalAlignment.Top;
			children.Add(testText);

		}

		private void DropNewThing(Answer answer, double newSize, Color newColor, ThingType tType)
		{
			// Only drop within the center "square" area 
			double dropWidth = 1720;// sceneRect.Bottom - sceneRect.Top;
			//if (dropWidth > sceneRect.Right - sceneRect.Left)
			//{
			//	dropWidth = sceneRect.Right - sceneRect.Left;
			//}

			double xPosition;
			//Keep getting a new position until we're not close to an existing thing
			do
			{
				xPosition = (rnd.NextDouble() * dropWidth) + ((sceneRect.Left + sceneRect.Right - dropWidth) / 2);
			}
			while (IsThisThingTooCloseToAnotherThing(xPosition));

			//Point centerDropPoint = new Point(xPosition, sceneRect.Top - newSize);

			double[] randomNumbers = { -50, -100, -150, -200, -300, -400, -500, -75, -600 };

			double yPosition = randomNumbers[randomYPosition.Next(0, randomNumbers.Length - 1)];

			Point centerDropPoint = new Point(xPosition, yPosition);
			var newThing = new Thing
			{
				Size = newSize,
				YVelocity = ((0.5 * rnd.NextDouble()) - 0.25) / targetFrameRate,
				XVelocity = 0,
				Center = centerDropPoint,
				SpinRate = ((rnd.NextDouble() * 12.0) - 6.0) * 2.0 * Math.PI / targetFrameRate / 4.0,
				Theta = 0,
				TimeLastHit = DateTime.MinValue,
				AvgTimeBetweenHits = 100,
				Color = newColor,
				Brush = null,
				Brush2 = null,
				BrushPulse = null,
				Dissolve = 0,
				State = ThingState.Falling,
				TouchedBy = 0,
				Hotness = 0,
				FlashCount = 0,
				dropItemIndex = dropItems.IndexOf(answer),
				correctAnswer = answer.AnsCorrect,
				thingType = tType
			};

			bool alreadyAdded = false;


			//xxx optimize
			foreach (Thing thing in things)
			{
				if (thing.dropItemIndex == dropItems.IndexOf(answer))
					alreadyAdded = true;
			}

			if (!alreadyAdded)
				things.Add(newThing);
		}

		private bool IsThisThingTooCloseToAnotherThing(double newX)
		{
			return things.Any(thing => newX <= thing.Center.X + 100 && newX >= thing.Center.X - 100);
		}

		private Shape MakeSimpleShape(
			int numSides,
			double size,
			double spin,
			Point center,
			Brush brush,
			Brush brushStroke,
			double strokeThickness,
			double opacity,
			string imagePath)
		{
			if (numSides <= 1)
			{
				var circle = new Ellipse { Width = size * 2, Height = size * 2, Stroke = brushStroke };
				if (circle.Stroke != null)
				{
					circle.Stroke.Opacity = opacity;
				}

				string imageURI = @"pack://application:,,/Resources/" + imagePath;

				ImageBrush myBrush = new ImageBrush { ImageSource = new BitmapImage(new Uri(imageURI)) };
				circle.Fill = myBrush;
				circle.StrokeThickness = 4;
				//circle.Fill = (numSides == 1) ? brush : null;
				circle.SetValue(Canvas.LeftProperty, center.X - size);
				circle.SetValue(Canvas.TopProperty, center.Y - size);
				return circle;
			}

			var points = new PointCollection(numSides + 2);
			double theta = spin;
			for (int i = 0; i <= numSides + 1; ++i)
			{
				points.Add(new Point((Math.Cos(theta) * size) + center.X, (Math.Sin(theta) * size) + center.Y));
				theta = theta + (2.0 * Math.PI * 1 / numSides);
			}

			var polyline = new Polyline { Points = points, Stroke = brushStroke };
			if (polyline.Stroke != null)
			{
				polyline.Stroke.Opacity = opacity;
			}

			polyline.Fill = brush;
			polyline.FillRule = FillRule.Nonzero;
			polyline.StrokeThickness = strokeThickness;
			return polyline;
		}

		#endregion

		static internal ImageSource doGetImageSourceFromResource(string psAssemblyName, string psResourceName)
		{
			Uri oUri = new Uri("pack://application:,,,/" + psAssemblyName + ";component/" + psResourceName, UriKind.RelativeOrAbsolute);
			return BitmapFrame.Create(oUri);
		}

		public static Label MakeSimpleLabel(string text, Rect bounds, Brush brush)
		{
			Label label = new Label { Content = text };

			if (text.ToUpper().Contains("?"))
				label.Background = new SolidColorBrush(Colors.Black);

			if (bounds.Width != 0)
			{
				label.SetValue(Canvas.LeftProperty, bounds.Left);
				label.SetValue(Canvas.TopProperty, bounds.Top);
				label.Width = bounds.Width;
				label.Height = bounds.Height;
			}

			label.Foreground = brush;
			label.FontFamily = new FontFamily("Arial");
			label.FontWeight = FontWeight.FromOpenTypeWeight(600);
			label.FontStyle = FontStyles.Normal;
			label.HorizontalAlignment = HorizontalAlignment.Center;
			label.VerticalAlignment = VerticalAlignment.Center;
			return label;
		}

		private static double SquaredDistance(double x1, double y1, double x2, double y2)
		{
			return ((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1));
		}

		private void AddToScore(int player, int points, Point center)
		{
			if (scores.ContainsKey(player))
			{
				scores[player] = scores[player] + points;
			}
			else
			{
				scores.Add(player, points);
			}

			FlyingText.NewFlyingText(sceneRect.Width / 300, center, "+" + points);

		}

		internal class DropItem
		{
			public DropType dropType;
			public int Sides;
			public string ImagePath;
			public int thingType;
			public bool ShouldThisBeDropped;
			public int ID;
		}

		public enum ThingType
		{
			Difficulty = 1,
			PlayerName = 2,
			Answer = 3
		}

		// The Thing struct represents a single object that is flying through the air, and
		// all of its properties.
		private struct Thing
		{
			public Point Center;
			public double Size;
			public double Theta;
			public double SpinRate;
			public double YVelocity;
			public double XVelocity;
			public Color Color;
			public Brush Brush;
			public Brush Brush2;
			public Brush BrushPulse;
			public double Dissolve;
			public ThingState State;
			public DateTime TimeLastHit;
			public double AvgTimeBetweenHits;
			public int TouchedBy;               // Last player to touch this thing
			public int Hotness;                 // Score level
			public int FlashCount;
			public int dropItemIndex;
			public bool? correctAnswer;
			public ThingType thingType;

			// Hit testing between this thing and a single segment.  If hit, the center point on
			// the segment being hit is returned, along with the spot on the line from 0 to 1 if
			// a line segment was hit.
			public bool Hit(Segment seg, ref Point hitCenter, ref double lineHitLocation)
			{
				double minDxSquared = Size + seg.Radius;
				minDxSquared *= minDxSquared;

				// See if falling thing hit this body segment
				if (seg.IsCircle())
				{
					if (SquaredDistance(Center.X, Center.Y, seg.X1, seg.Y1) <= minDxSquared)
					{
						hitCenter.X = seg.X1;
						hitCenter.Y = seg.Y1;
						lineHitLocation = 0;
						return true;
					}
				}
				else
				{
					double sqrLineSize = SquaredDistance(seg.X1, seg.Y1, seg.X2, seg.Y2);
					if (sqrLineSize < 0.5)
					{
						// if less than 1/2 pixel apart, just check dx to an endpoint
						return SquaredDistance(Center.X, Center.Y, seg.X1, seg.Y1) < minDxSquared;
					}

					// Find dx from center to line
					double u = ((Center.X - seg.X1) * (seg.X2 - seg.X1)) + (((Center.Y - seg.Y1) * (seg.Y2 - seg.Y1)) / sqrLineSize);
					if ((u >= 0) && (u <= 1.0))
					{   // Tangent within line endpoints, see if we're close enough
						double intersectX = seg.X1 + ((seg.X2 - seg.X1) * u);
						double intersectY = seg.Y1 + ((seg.Y2 - seg.Y1) * u);

						if (SquaredDistance(Center.X, Center.Y, intersectX, intersectY) < minDxSquared)
						{
							lineHitLocation = u;
							hitCenter.X = intersectX;
							hitCenter.Y = intersectY;
							return true;
						}
					}
					else
					{
						// See how close we are to an endpoint
						if (u < 0)
						{
							if (SquaredDistance(Center.X, Center.Y, seg.X1, seg.Y1) < minDxSquared)
							{
								lineHitLocation = 0;
								hitCenter.X = seg.X1;
								hitCenter.Y = seg.Y1;
								return true;
							}
						}
						else
						{
							if (SquaredDistance(Center.X, Center.Y, seg.X2, seg.Y2) < minDxSquared)
							{
								lineHitLocation = 1;
								hitCenter.X = seg.X2;
								hitCenter.Y = seg.Y2;
								return true;
							}
						}
					}

					return false;
				}

				return false;
			}

			// Change our velocity based on the object's velocity, our velocity, and where we hit.
			public void BounceOff(double x1, double y1, double otherSize, double fXv, double fYv)
			{
				double x0 = Center.X;
				double y0 = Center.Y;
				double xv0 = XVelocity - fXv;
				double yv0 = YVelocity - fYv;
				double dist = otherSize + Size;
				double dx = Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));
				double xdif = x1 - x0;
				double ydif = y1 - y0;
				double newvx1 = 0;
				double newvy1 = 0;

				x0 = x1 - (xdif / dx * dist);
				y0 = y1 - (ydif / dx * dist);
				xdif = x1 - x0;
				ydif = y1 - y0;

				double bsq = dist * dist;
				double b = dist;
				double asq = (xv0 * xv0) + (yv0 * yv0);
				double a = Math.Sqrt(asq);
				if (a > 0.000001)
				{
					// if moving much at all...
					double cx = x0 + xv0;
					double cy = y0 + yv0;
					double csq = ((x1 - cx) * (x1 - cx)) + ((y1 - cy) * (y1 - cy));
					double tt = asq + bsq - csq;
					double bb = 2 * a * b;
					double power = a * (tt / bb);
					newvx1 -= 2 * (xdif / dist * power);
					newvy1 -= 2 * (ydif / dist * power);
				}

				XVelocity += newvx1;
				YVelocity += newvy1;
				Center.X = x0;
				Center.Y = y0;
			}
		}
	}
}