
namespace DiscrepancyScanner
{
    partial class PrefrencesForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.numericFy = new System.Windows.Forms.NumericUpDown();
            this.label8 = new System.Windows.Forms.Label();
            this.numericTopRight = new System.Windows.Forms.NumericUpDown();
            this.label4 = new System.Windows.Forms.Label();
            this.numericTopMid = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            this.numericTopLeft = new System.Windows.Forms.NumericUpDown();
            this.label6 = new System.Windows.Forms.Label();
            this.numericBotRight = new System.Windows.Forms.NumericUpDown();
            this.label3 = new System.Windows.Forms.Label();
            this.numericBotMid = new System.Windows.Forms.NumericUpDown();
            this.label2 = new System.Windows.Forms.Label();
            this.numericBotLeft = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.numericLocationTolerance = new System.Windows.Forms.NumericUpDown();
            this.label7 = new System.Windows.Forms.Label();
            this.numericLengthTolerance = new System.Windows.Forms.NumericUpDown();
            this.label12 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.checkAttached = new System.Windows.Forms.CheckBox();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.checkOverLay = new System.Windows.Forms.CheckBox();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericFy)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericTopRight)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericTopMid)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericTopLeft)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericBotRight)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericBotMid)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericBotLeft)).BeginInit();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericLocationTolerance)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericLengthTolerance)).BeginInit();
            this.groupBox3.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.numericFy);
            this.groupBox1.Controls.Add(this.label8);
            this.groupBox1.Controls.Add(this.numericTopRight);
            this.groupBox1.Controls.Add(this.label4);
            this.groupBox1.Controls.Add(this.numericTopMid);
            this.groupBox1.Controls.Add(this.label5);
            this.groupBox1.Controls.Add(this.numericTopLeft);
            this.groupBox1.Controls.Add(this.label6);
            this.groupBox1.Controls.Add(this.numericBotRight);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.numericBotMid);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.numericBotLeft);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Location = new System.Drawing.Point(14, 95);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(255, 136);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Design Check Utilization Factors";
            // 
            // numericFy
            // 
            this.numericFy.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericFy.Location = new System.Drawing.Point(167, 100);
            this.numericFy.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.numericFy.Minimum = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.numericFy.Name = "numericFy";
            this.numericFy.Size = new System.Drawing.Size(80, 20);
            this.numericFy.TabIndex = 13;
            this.numericFy.Value = new decimal(new int[] {
            400,
            0,
            0,
            0});
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(5, 103);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(150, 13);
            this.label8.TabIndex = 12;
            this.label8.Text = "Links Fy (bars more than 8mm)";
            // 
            // numericTopRight
            // 
            this.numericTopRight.DecimalPlaces = 2;
            this.numericTopRight.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericTopRight.Location = new System.Drawing.Point(196, 71);
            this.numericTopRight.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.numericTopRight.Name = "numericTopRight";
            this.numericTopRight.Size = new System.Drawing.Size(53, 20);
            this.numericTopRight.TabIndex = 11;
            this.numericTopRight.Value = new decimal(new int[] {
            95,
            0,
            0,
            131072});
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(139, 73);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(54, 13);
            this.label4.TabIndex = 10;
            this.label4.Text = "Top Right";
            // 
            // numericTopMid
            // 
            this.numericTopMid.DecimalPlaces = 2;
            this.numericTopMid.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericTopMid.Location = new System.Drawing.Point(196, 46);
            this.numericTopMid.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.numericTopMid.Name = "numericTopMid";
            this.numericTopMid.Size = new System.Drawing.Size(53, 20);
            this.numericTopMid.TabIndex = 9;
            this.numericTopMid.Value = new decimal(new int[] {
            105,
            0,
            0,
            131072});
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(139, 48);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(46, 13);
            this.label5.TabIndex = 8;
            this.label5.Text = "Top Mid";
            // 
            // numericTopLeft
            // 
            this.numericTopLeft.DecimalPlaces = 2;
            this.numericTopLeft.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericTopLeft.Location = new System.Drawing.Point(196, 20);
            this.numericTopLeft.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.numericTopLeft.Name = "numericTopLeft";
            this.numericTopLeft.Size = new System.Drawing.Size(53, 20);
            this.numericTopLeft.TabIndex = 7;
            this.numericTopLeft.Value = new decimal(new int[] {
            95,
            0,
            0,
            131072});
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(139, 22);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(47, 13);
            this.label6.TabIndex = 6;
            this.label6.Text = "Top Left";
            // 
            // numericBotRight
            // 
            this.numericBotRight.DecimalPlaces = 2;
            this.numericBotRight.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericBotRight.Location = new System.Drawing.Point(65, 71);
            this.numericBotRight.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.numericBotRight.Name = "numericBotRight";
            this.numericBotRight.Size = new System.Drawing.Size(53, 20);
            this.numericBotRight.TabIndex = 5;
            this.numericBotRight.Value = new decimal(new int[] {
            95,
            0,
            0,
            131072});
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(8, 73);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(51, 13);
            this.label3.TabIndex = 4;
            this.label3.Text = "Bot Right";
            // 
            // numericBotMid
            // 
            this.numericBotMid.DecimalPlaces = 2;
            this.numericBotMid.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericBotMid.Location = new System.Drawing.Point(65, 46);
            this.numericBotMid.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.numericBotMid.Name = "numericBotMid";
            this.numericBotMid.Size = new System.Drawing.Size(53, 20);
            this.numericBotMid.TabIndex = 3;
            this.numericBotMid.Value = new decimal(new int[] {
            9,
            0,
            0,
            65536});
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 48);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(43, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Bot Mid";
            // 
            // numericBotLeft
            // 
            this.numericBotLeft.DecimalPlaces = 2;
            this.numericBotLeft.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericBotLeft.Location = new System.Drawing.Point(65, 20);
            this.numericBotLeft.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.numericBotLeft.Name = "numericBotLeft";
            this.numericBotLeft.Size = new System.Drawing.Size(53, 20);
            this.numericBotLeft.TabIndex = 1;
            this.numericBotLeft.Value = new decimal(new int[] {
            95,
            0,
            0,
            131072});
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 22);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(44, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Bot Left";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.numericLocationTolerance);
            this.groupBox2.Controls.Add(this.label7);
            this.groupBox2.Controls.Add(this.numericLengthTolerance);
            this.groupBox2.Controls.Add(this.label12);
            this.groupBox2.Location = new System.Drawing.Point(12, 12);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(255, 77);
            this.groupBox2.TabIndex = 12;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Frame Matching Prefrences";
            // 
            // numericLocationTolerance
            // 
            this.numericLocationTolerance.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericLocationTolerance.Location = new System.Drawing.Point(169, 45);
            this.numericLocationTolerance.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.numericLocationTolerance.Minimum = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.numericLocationTolerance.Name = "numericLocationTolerance";
            this.numericLocationTolerance.Size = new System.Drawing.Size(80, 20);
            this.numericLocationTolerance.TabIndex = 3;
            this.numericLocationTolerance.Value = new decimal(new int[] {
            2000,
            0,
            0,
            0});
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(7, 48);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(156, 13);
            this.label7.TabIndex = 2;
            this.label7.Text = "Frame Location Tolerance (mm)";
            // 
            // numericLengthTolerance
            // 
            this.numericLengthTolerance.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericLengthTolerance.Location = new System.Drawing.Point(169, 19);
            this.numericLengthTolerance.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.numericLengthTolerance.Minimum = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.numericLengthTolerance.Name = "numericLengthTolerance";
            this.numericLengthTolerance.Size = new System.Drawing.Size(80, 20);
            this.numericLengthTolerance.TabIndex = 1;
            this.numericLengthTolerance.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(8, 22);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(148, 13);
            this.label12.TabIndex = 0;
            this.label12.Text = "Frame Length Tolerance (mm)";
            // 
            // button1
            // 
            this.button1.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.button1.Location = new System.Drawing.Point(87, 423);
            this.button1.MaximumSize = new System.Drawing.Size(100, 30);
            this.button1.MinimumSize = new System.Drawing.Size(100, 30);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(100, 30);
            this.button1.TabIndex = 13;
            this.button1.Text = "Apply";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.richTextBox1);
            this.groupBox3.Location = new System.Drawing.Point(14, 309);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(255, 109);
            this.groupBox3.TabIndex = 12;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "REVIT Ignore List (Separated by comma)";
            // 
            // richTextBox1
            // 
            this.richTextBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.richTextBox1.Location = new System.Drawing.Point(3, 16);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.Size = new System.Drawing.Size(249, 90);
            this.richTextBox1.TabIndex = 0;
            this.richTextBox1.Text = "parapet , ledge";
            // 
            // checkAttached
            // 
            this.checkAttached.AutoSize = true;
            this.checkAttached.Checked = true;
            this.checkAttached.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkAttached.Location = new System.Drawing.Point(10, 17);
            this.checkAttached.Name = "checkAttached";
            this.checkAttached.Size = new System.Drawing.Size(163, 17);
            this.checkAttached.TabIndex = 14;
            this.checkAttached.Text = "Include Attached Revit Links";
            this.checkAttached.UseVisualStyleBackColor = true;
            this.checkAttached.Visible = false;
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.checkOverLay);
            this.groupBox4.Controls.Add(this.checkAttached);
            this.groupBox4.Location = new System.Drawing.Point(14, 237);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(255, 66);
            this.groupBox4.TabIndex = 13;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "Include Linked REVIT Dcouments ?";
            // 
            // checkOverLay
            // 
            this.checkOverLay.AutoSize = true;
            this.checkOverLay.Location = new System.Drawing.Point(9, 40);
            this.checkOverLay.Name = "checkOverLay";
            this.checkOverLay.Size = new System.Drawing.Size(160, 17);
            this.checkOverLay.TabIndex = 15;
            this.checkOverLay.Text = "Include OverLay Revit Links";
            this.checkOverLay.UseVisualStyleBackColor = true;
            this.checkOverLay.Visible = false;
            // 
            // PrefrencesForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(280, 459);
            this.Controls.Add(this.groupBox4);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Name = "PrefrencesForm";
            this.Text = "PrefrencesForm";
            this.TopMost = true;
            this.Load += new System.EventHandler(this.PrefrencesForm_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericFy)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericTopRight)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericTopMid)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericTopLeft)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericBotRight)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericBotMid)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericBotLeft)).EndInit();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericLocationTolerance)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericLengthTolerance)).EndInit();
            this.groupBox3.ResumeLayout(false);
            this.groupBox4.ResumeLayout(false);
            this.groupBox4.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.NumericUpDown numericBotRight;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.NumericUpDown numericBotMid;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.NumericUpDown numericBotLeft;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NumericUpDown numericTopRight;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.NumericUpDown numericTopMid;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.NumericUpDown numericTopLeft;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.NumericUpDown numericLengthTolerance;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.NumericUpDown numericLocationTolerance;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.CheckBox checkAttached;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.CheckBox checkOverLay;
        private System.Windows.Forms.NumericUpDown numericFy;
        private System.Windows.Forms.Label label8;
    }
}